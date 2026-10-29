using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Wall clock: hands rotate clockwise about the clock's forward axis (RmClock HourHand / MinuteHand).</summary>
    public sealed class RoomClock : MonoBehaviour
    {
        public Transform Hour, Minute;
        float _shown = -1f;

        /// <summary>Hours since midnight (e.g. 9.5 = 09:30).</summary>
        public void SetTime(float hours)
        {
            if (Mathf.Abs(hours - _shown) < 0.004f) return;
            _shown = hours;
            float m = (hours - Mathf.Floor(hours)) * 60f;
            float h = hours % 12f;
            // seen from the front (the face looks along -forward), clockwise = negative angle about +forward
            if (Minute != null) Minute.localRotation = Quaternion.AngleAxis(-m * 6f, Vector3.forward);
            if (Hour != null) Hour.localRotation = Quaternion.AngleAxis(-h * 30f, Vector3.forward);
        }
    }
}
