using BirimizKovulacak.Core;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v3 office-phase UI (owner: desk UI track): computer apps, desk phone, morning call, round intro/play/reveal,
    /// onboarding steps. Created by <see cref="UIRoot"/>; visible during Morning..RoundReveal. Placeholder body.
    /// </summary>
    public sealed class DeskUi : UiKit.Screen
    {
        public DeskUi(VisualElement parent) : base(parent, "desk-ui") { Root.pickingMode = PickingMode.Ignore; }

        /// <summary>True when the UI needs a free cursor (computer/phone open, a modal card waiting for a click).</summary>
        public bool WantsCursor => false;

        public override void Tick() { }
    }
}
