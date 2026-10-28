using System;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    // Thin seams between the "feel" layer (camera, visibility, input, audio, VFX) and modules owned by other agents.
    // Everything here compiles without those modules; each implementation is optional and documented in docs/FEEL.md.

    /// <summary>Facial expression requests from the feel layer; the characters module maps them to its face atlas.</summary>
    public enum FeelExpression : byte
    {
        Neutral = 0,
        Nervous = 1,  // manager close while carrying something suspicious
        Shocked = 2,  // the manager just reacted to this player / tasted salt
        Happy = 3,    // task done, won
        Angry = 4,
        Sad = 5,      // fired
        Smug = 6,
    }

    /// <summary>
    /// Implemented by the characters module (PlayerAvatar itself or a component on the avatar root). All calls are
    /// cosmetic, idempotent and may arrive every frame; implementations must not allocate.
    /// </summary>
    public interface IAvatarFeel
    {
        void SetExpression(FeelExpression expression);
        /// <summary>Mouth flap / speaking indicator (driven by the voice module through FeelHooks.SetTalking).</summary>
        void SetTalking(bool talking);
        /// <summary>One-shot emote animation; called once per emote start (state-driven, never repeated).</summary>
        void PlayEmote(EmoteId emote);
        /// <summary>Sneak pose. Local avatar: immediate; remote: from the replicated crouch flag.</summary>
        void SetCrouched(bool crouched);
        /// <summary>
        /// Line-of-sight fade, 0 = invisible, 1 = fully visible. Return true when the avatar faded every renderer it owns
        /// (e.g. dithered materials); return false to let AvatarVisibility toggle the renderers at alpha 0.5.
        /// </summary>
        bool SetVisibility(float alpha);
        /// <summary>Small squash-and-stretch impulse (stop / land). Amount ~0.1..0.3.</summary>
        void Squash(float amount);
    }

    /// <summary>Implemented by the map module's OccluderWall (one per opaque wall/partition renderer).</summary>
    public interface IOccluderFadable
    {
        void SetFaded(bool faded);
    }

    /// <summary>Implemented by the UI module (register with <c>FeelHooks.Ui = this</c>).</summary>
    public interface IFeelUi
    {
        /// <summary>True while a text field has keyboard focus (suppresses call/emote hotkeys).</summary>
        bool TextInputFocused { get; }
        /// <summary>True if the UI draws the emote wheel from LocalPlayerController's wheel state (otherwise an IMGUI fallback is drawn).</summary>
        bool DrawsEmoteWheel { get; }
        /// <summary>
        /// Opens the fake-email template picker for the given pc. Call <paramref name="onPicked"/> exactly once with the
        /// chosen template index in [0, Email.TemplateCount), or -1 when cancelled.
        /// </summary>
        void PickEmailTemplate(string pcTargetId, Action<int> onPicked);
    }

    /// <summary>Static hook-up points for other modules.</summary>
    public static class FeelHooks
    {
        /// <summary>Set by the UI module.</summary>
        public static IFeelUi Ui;
        /// <summary>When true the UI plays the results stamp itself (call FeelHooks.ResultsStamp at the moment it lands).</summary>
        public static bool UiDrivesResultsStamp;

        public static bool TextInputFocused => Ui != null && Ui.TextInputFocused;

        /// <summary>The characters module's feel interface on an avatar, or null.</summary>
        public static IAvatarFeel Of(PlayerAvatar avatar)
        {
            if (avatar == null) return null;
            IAvatarFeel f;
            return avatar.TryGetComponent(out f) ? f : null;
        }

        /// <summary>Voice module: speaking state of a player (cosmetic mouth / indicator).</summary>
        public static void SetTalking(int slot, bool talking)
        {
            var f = Of(PlayerAvatar.BySlot(slot));
            if (f != null) f.SetTalking(talking);
        }

        /// <summary>UI module: the results stamp just landed at this screen position (pixels, origin bottom-left).</summary>
        public static void ResultsStamp(Vector2 screenPosition)
        {
            Sfx.Play("stamp");
            FxPool.StampBurstAtScreen(screenPosition);
            if (CameraRig.I != null) CameraRig.I.Punch(Vector3.forward, 0.6f);
        }
    }

    /// <summary>Layer numbers this module needs that are not (yet) in <see cref="Layers"/>.</summary>
    public static class FeelLayers
    {
        /// <summary>~1.3 m cubicle partitions (map v2). Blocks avatar line of sight, not glass.</summary>
        public const int Partitions = Layers.Partitions;
        public static int SightBlockers => (1 << Layers.Walls) | (1 << Partitions);
        public static int CameraOccluders => (1 << Layers.Walls) | (1 << Partitions);
        public static int SoundBlockers => 1 << Layers.Walls;
    }
}
