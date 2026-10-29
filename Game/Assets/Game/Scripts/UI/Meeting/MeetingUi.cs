using BirimizKovulacak.Core;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v3 meeting/final/week-results UI (owner: meeting UI track): dossier on the projector, recording playback,
    /// discussion, voting, warning stamp, defense spotlight, final vote, firing letter, week results. Created by
    /// <see cref="UIRoot"/>; visible from MeetingDossier to WeekResults. Placeholder body.
    /// </summary>
    public sealed class MeetingUi : UiKit.Screen
    {
        public MeetingUi(VisualElement parent) : base(parent, "meeting-ui") { Root.pickingMode = PickingMode.Ignore; }

        /// <summary>True when the UI needs a free cursor (voting list, results buttons).</summary>
        public bool WantsCursor => false;

        public override void Tick() { }
    }
}
