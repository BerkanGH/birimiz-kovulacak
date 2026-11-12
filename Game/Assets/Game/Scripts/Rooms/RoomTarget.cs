using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Something the first-person crosshair can point at: a desk target in the office or a colleague's seat in the
    /// meeting. Lives on the GameObject that owns the collider (built by ProjectBuilder.Rooms). Hover feedback: additive
    /// overlay copies of the prop's meshes (same transforms, ZTest LEqual) or the bean's outline colour.
    /// </summary>
    public sealed class RoomTarget : MonoBehaviour
    {
        public DeskTarget Desk = DeskTarget.None;
        /// <summary>Meeting seat index (0..5); the meeting room maps it to the player's slot. -1 = not a seat.</summary>
        public int Seat = -1;
        /// <summary>v5 step 3a: a meeting object (the local folder, the ballot paper, a whiteboard).</summary>
        public MeetingTarget Meet = MeetingTarget.None;
        public Renderer[] Overlays = new Renderer[0];
        public SeatedBean Bean;

        bool _hover;

        public bool Hovered => _hover;

        public void SetHover(bool on)
        {
            if (_hover == on) return;
            _hover = on;
            for (int i = 0; i < Overlays.Length; i++) if (Overlays[i] != null) Overlays[i].enabled = on;
            if (Bean != null) Bean.SetHover(on);
        }

        void OnDisable() { SetHover(false); }
    }
}
