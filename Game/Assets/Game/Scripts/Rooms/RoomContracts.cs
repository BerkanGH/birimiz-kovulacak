using System;
using System.Collections.Generic;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    // v3 seam between the 3D world (owner: world track, Scripts/Rooms/**) and its users (lead integration, desk UI,
    // meeting UI). The world implements IRoomPresenter and registers it in Rooms.Presenter when the scene loads;
    // everybody else only talks to these types. See docs/V3_DESIGN.md §9.

    /// <summary>Clickable things in the local player's office (v5 appends the recorder, the cork board and the chair).</summary>
    public enum DeskTarget { None, Monitor, Phone, Mug, Window, Drawer, Nameplate, Recorder, Board, Chair }

    /// <summary>Where the first-person camera is and what the mouse does.</summary>
    public enum PovMode
    {
        /// <summary>Seated in your office: mouse-look with limits, crosshair, click desk targets.</summary>
        Office,
        /// <summary>Camera framed on the monitor; cursor free (the computer UI is open).</summary>
        Monitor,
        /// <summary>Camera framed on the desk phone; cursor free (the phone UI is open).</summary>
        Phone,
        /// <summary>Seated at the meeting table: mouse-look, crosshair, click colleagues.</summary>
        Meeting,
        /// <summary>Meeting camera framed on the projector screen; cursor free.</summary>
        MeetingProjector,
        /// <summary>Scripted camera (warning stamp, firing); no input.</summary>
        Cutscene,
        /// <summary>v5: camera framed on the office cork board (suç tahtası); no mouse-look.</summary>
        Board,
    }

    public enum MonitorState { Normal, Alert, Offline, Coffee, Off }

    /// <summary>OffHook (v5): the handset is up (phone panel open) but no call is running.</summary>
    public enum PhoneLineState { Idle, Ringing, Dialing, InCall, Dead, OffHook }

    public enum MeetingReaction { None, Nod, Laugh, Gasp, Point, Slump, Cheer }

    /// <summary>One occupied seat at the meeting table.</summary>
    public struct MeetingSeatInfo
    {
        public int Slot;
        public int ColorIndex;
        public string Name;
        public bool Connected;
    }

    /// <summary>Implemented by the world (rooms scene). All calls are cheap and idempotent; call them every frame if convenient.</summary>
    public interface IRoomPresenter
    {
        /// <summary>Show the local player's own office, personalised (colour accents, nameplate, decor variant).</summary>
        void EnterOffice(int localSlot, int colorIndex, string playerName);
        /// <summary>Show the meeting room with these seats; the local player sits at its own seat.</summary>
        void EnterMeeting(int localSlot, IReadOnlyList<MeetingSeatInfo> seats, string managerProfileId);
        PovMode Mode { get; }
        void SetPovMode(PovMode mode);
        void SetPhoneState(PhoneLineState state);
        void SetMonitorState(MonitorState state);
        /// <summary>0 = morning light, 1 = evening light (the lead maps phases of the day onto it).</summary>
        void SetTimeOfDay(float t01);
        /// <summary>Voice level 0..1 of a seated player (meeting talk animation).</summary>
        void SetSpeaking(int slot, float level);
        /// <summary>Defense spotlight on a seat, -1 for none.</summary>
        void SetSpotlight(int slot);
        void React(int slot, MeetingReaction reaction);
        void SetManagerTalking(bool talking);
        /// <summary>The fired player's avatar slumps and holds a cardboard box; -1 clears.</summary>
        void SetFired(int slot);
        /// <summary>Texture shown on the meeting projector screen (the meeting UI renders the dossier into it); null = default slide.</summary>
        void SetProjectorTexture(Texture texture);
        /// <summary>Camera used for the first-person view (UI may need it for world-to-screen anchors).</summary>
        Camera PovCamera { get; }
    }

    public static class Rooms
    {
        /// <summary>Set by the world when the rooms scene is ready; null before.</summary>
        public static IRoomPresenter Presenter;

        /// <summary>Raised by the world when the local player clicks a desk target in <see cref="PovMode.Office"/>.</summary>
        public static event Action<DeskTarget> DeskClicked;
        /// <summary>Raised by the world when the local player clicks a colleague's avatar in <see cref="PovMode.Meeting"/> (slot).</summary>
        public static event Action<int> SeatClicked;
        /// <summary>Desk target under the crosshair (for the hover hint), None if nothing.</summary>
        public static DeskTarget Hovered;
        /// <summary>Seat under the crosshair in the meeting, -1 if none.</summary>
        public static int HoveredSeat = -1;

        public static void RaiseDeskClicked(DeskTarget t) { DeskClicked?.Invoke(t); }
        public static void RaiseSeatClicked(int slot) { SeatClicked?.Invoke(slot); }

        /// <summary>PlayerPrefs keys the world reads (the settings UI writes them).</summary>
        public const string PrefSensitivity = "pov.sensitivity", PrefInvertY = "pov.invert_y", PrefFov = "pov.fov";
    }
}
