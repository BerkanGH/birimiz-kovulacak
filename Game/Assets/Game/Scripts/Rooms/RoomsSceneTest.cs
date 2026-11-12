using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// World test harness: <c>-scenetest [-scenetestout &lt;dir&gt;]</c>. Hides the UI, drives <see cref="Rooms.Presenter"/>
    /// the way a match does (office states, focus modes, morning/evening, a six-seat meeting with talking, reactions,
    /// spotlight, firing, every manager), saves numbered screenshots plus memory.txt and quits.
    /// </summary>
    public sealed class RoomsSceneTest : MonoBehaviour
    {
        string _out;
        int _n;
        readonly List<string> _log = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            bool on = false;
            string dir = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-scenetest", StringComparison.OrdinalIgnoreCase)) on = true;
                if (string.Equals(args[i], "-scenetestout", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) dir = args[i + 1];
            }
            if (!on) return;
            var go = new GameObject("RoomsSceneTest");
            var t = go.AddComponent<RoomsSceneTest>();
            t._out = Path.GetFullPath(string.IsNullOrEmpty(dir) ? Path.Combine(Application.persistentDataPath, "scenetest") : dir);
        }

        void LateUpdate()
        {
            // keep every UI document except the crosshair hidden (UIRoot rebuilds its screens each frame)
            foreach (var d in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (d.gameObject.name != "Crosshair" && d.rootVisualElement != null && d.rootVisualElement.style.display != DisplayStyle.None)
                    d.rootVisualElement.style.display = DisplayStyle.None;
        }

        IEnumerator Shot(string name, float settle = 1.0f)
        {
            yield return new WaitForSeconds(settle);
            yield return new WaitForEndOfFrame();
            _n++;
            string file = Path.Combine(_out, string.Format("{0:00}_{1}.png", _n, name));
            ScreenCapture.CaptureScreenshot(file);
            _log.Add(file);
            Debug.Log("[SceneTest] " + file);
            yield return null;
            yield return null;
        }

        IEnumerator WaitRingBurst()
        {
            // RingBurst windows: [0, 0.42) and (0.62, 1.04) of a 3 s cycle
            float t0 = Time.time;
            while (Time.time - t0 < 3.5f)
            {
                float p = Mathf.Repeat(Time.time, 3f);
                if (p > 0.18f && p < 0.3f) yield break;
                yield return null;
            }
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(_out);
            Debug.Log("[SceneTest] output " + _out);
            float until = Time.realtimeSinceStartup + 20f;
            while (RoomsPresenter.Instance == null && Time.realtimeSinceStartup < until) yield return null;
            var p = RoomsPresenter.Instance;
            if (p == null) { Debug.LogError("[SceneTest] no RoomsPresenter"); Application.Quit(2); yield break; }
            IRoomPresenter rooms = p;
            Rooms.DeskClicked += t => Debug.Log("[SceneTest] DeskClicked " + t);
            Rooms.SeatClicked += s => Debug.Log("[SceneTest] SeatClicked " + s);
            RoomsPresenter.ForceLookInput = true;
            yield return new WaitForSeconds(1.5f);

            // ---------------------------------------------------------------- office
            rooms.EnterOffice(0, 0, "Deniz");
            rooms.SetPovMode(PovMode.Office);
            rooms.SetPhoneState(PhoneLineState.Idle);
            rooms.SetMonitorState(MonitorState.Normal);
            rooms.SetTimeOfDay(0.05f);
            p.SetLook(0f, 13f);
            yield return Shot("office_morning", 2.0f);
            p.SetLook(-72f, 4f);
            yield return Shot("office_window_morning");
            p.SetLook(96f, 4f);
            p.Office.PassBy(0.3f, 1f);
            yield return Shot("office_door_passerby", 0.35f);
            var sil = p.Office.Silhouette;
            var silR = sil != null ? sil.GetComponent<Renderer>() : null;
            Debug.Log("[SceneTest] silhouette active=" + (sil != null && sil.gameObject.activeInHierarchy) + " pos=" + (sil != null ? sil.position.ToString("F2") : "-")
                      + " rot=" + (sil != null ? sil.eulerAngles.ToString("F0") : "-") + " scale=" + (sil != null ? sil.lossyScale.ToString("F2") : "-")
                      + " enabled=" + (silR != null && silR.enabled) + " mat=" + (silR != null && silR.sharedMaterial != null ? silR.sharedMaterial.name + "/" + silR.sharedMaterial.shader.name + " q" + silR.sharedMaterial.renderQueue : "-"));
            // straight through the door glass, then from inside the corridor
            p.SetCutscenePose(new Vector3(0.9f, 1.45f, -0.85f), Quaternion.LookRotation(Vector3.right), 50f);
            rooms.SetPovMode(PovMode.Cutscene);
            p.Office.PassBy(0.38f, 1f);
            yield return Shot("debug_door_straight", 0.45f);
            p.SetCutscenePose(new Vector3(2.9f, 1.4f, -0.85f), Quaternion.LookRotation(Vector3.left), 60f);
            p.Office.PassBy(0.38f, 1f);
            yield return Shot("debug_corridor", 0.45f);
            p.ClearCutscenePose();
            rooms.SetPovMode(PovMode.Office);
            p.SetLook(0f, 13f);
            rooms.SetPhoneState(PhoneLineState.Ringing);
            yield return new WaitForSeconds(0.6f);
            yield return WaitRingBurst();
            yield return Shot("office_ringing", 0.0f);
            p.AimAt(p.Office.Phone.position + Vector3.up * 0.05f);
            yield return Shot("office_hover_phone", 0.8f);
            Debug.Log("[SceneTest] hovered " + Rooms.Hovered);
            p.ClickHovered();
            rooms.SetPhoneState(PhoneLineState.InCall);
            p.SetLook(0f, 13f);
            yield return Shot("office_in_call", 1.4f);
            p.SetLook(-25f, 4f);
            yield return Shot("office_in_call_lookleft", 0.6f);
            rooms.SetPhoneState(PhoneLineState.Dead);
            p.SetLook(14f, 24f);
            yield return Shot("office_dead_line", 1.2f);
            rooms.SetPhoneState(PhoneLineState.Idle);
            rooms.SetMonitorState(MonitorState.Alert);
            p.SetLook(-4f, 16f);
            yield return Shot("office_monitor_alert", 0.9f);
            rooms.SetMonitorState(MonitorState.Offline);
            yield return Shot("office_monitor_offline", 0.6f);
            rooms.SetMonitorState(MonitorState.Coffee);
            yield return Shot("office_monitor_coffee", 1.0f);
            rooms.SetMonitorState(MonitorState.Normal);
            p.AimAt(p.Office.Mug.position + Vector3.up * 0.06f);
            yield return Shot("office_hover_mug", 0.8f);
            p.ClickHovered();
            yield return Shot("office_click_mug_sip", 0.6f);
            p.AimAt(p.Office.Drawer.position + p.Office.Drawer.rotation * new Vector3(0f, 0f, -0.29f));
            yield return new WaitForSeconds(0.6f);
            p.ClickHovered();
            yield return Shot("office_click_drawer", 0.7f);
            p.SetLook(0f, 13f);
            rooms.SetPovMode(PovMode.Monitor);
            yield return Shot("mode_monitor", 1.3f);
            rooms.SetPovMode(PovMode.Phone);
            yield return Shot("mode_phone", 1.3f);
            rooms.SetPhoneState(PhoneLineState.Dialing);
            yield return Shot("mode_phone_dialing", 1.0f);
            rooms.SetPhoneState(PhoneLineState.Idle);
            rooms.SetPovMode(PovMode.Office);
            rooms.SetTimeOfDay(0.5f);
            yield return Shot("office_afternoon", 1.6f);
            rooms.SetTimeOfDay(1f);
            yield return Shot("office_evening", 1.2f);
            p.SetLook(-60f, 2f);
            yield return Shot("office_evening_window", 0.8f);
            p.SetLook(95f, 10f);
            yield return Shot("office_evening_door", 0.8f);
            rooms.EnterOffice(4, 4, "Kerem");
            rooms.SetTimeOfDay(0.2f);
            p.SetLook(-10f, 8f);
            yield return Shot("office_variant_slot4", 1.2f);
            p.SetLook(0f, 55f);
            yield return Shot("office_look_down", 0.8f);

            // ---------------------------------------------------------------- v5 office: recorder, standing, walking, door lamp, board
            rooms.EnterOffice(0, 0, "Deniz");
            rooms.SetTimeOfDay(0.3f);
            p.Office.SetRecorder(1, false, false);
            p.AimAt(p.Office.Recorder.position + Vector3.up * 0.04f);
            yield return Shot("v5_recorder_idle", 1.0f);
            Debug.Log("[SceneTest] hovered " + Rooms.Hovered);
            rooms.SetPhoneState(PhoneLineState.InCall);
            p.Office.SetRecorder(0, true, true);
            yield return Shot("v5_recorder_recording", 1.2f);
            p.Office.SetRecorder(0, false, true);
            rooms.SetPhoneState(PhoneLineState.Idle);
            rooms.SetPovMode(PovMode.Phone);
            rooms.SetPhoneState(PhoneLineState.OffHook);
            yield return Shot("v5_phone_offhook", 1.3f);
            rooms.SetPhoneState(PhoneLineState.Idle);
            rooms.SetPovMode(PovMode.Office);
            p.StandAt(p.Office.StandSpot, 0f, 24f);
            yield return Shot("v5_stand_at_desk", 1.2f);
            // walk into the desk: the walker must stop in front of it
            RoomsPresenter.ScriptedMove = new Vector2(0f, 1f);
            yield return new WaitForSeconds(1.2f);
            Debug.Log("[SceneTest] walk forward -> " + p.Walker.Position.ToString("F2"));
            RoomsPresenter.ScriptedMove = new Vector2(0.3f, -1f);
            yield return new WaitForSeconds(2.2f);
            RoomsPresenter.ScriptedMove = null;
            Debug.Log("[SceneTest] walk back -> " + p.Walker.Position.ToString("F2"));
            yield return Shot("v5_walked_back", 0.6f);
            p.StandAt(new Vector2(-1.45f, -1.7f), 32f, 12f);
            yield return Shot("v5_stand_back_corner", 1.0f);
            p.StandAt(new Vector2(-0.2f, -0.9f), 95f, 4f);
            p.Office.SetDoorLamp(true);
            yield return Shot("v5_door_lamp", 1.4f);
            p.Office.SetDoorLamp(false);
            p.StandAt(new Vector2(1.5f, -0.7f), -100f, 6f);
            yield return Shot("v5_stand_door_side", 1.0f);
            p.SitDown(false);
            rooms.SetPovMode(PovMode.Board);
            yield return Shot("v5_board_zoom", 1.4f);
            rooms.SetPovMode(PovMode.Office);
            p.SetLook(0f, 13f);

            // ---------------------------------------------------------------- v5 step 2: cash box, roster, drawer, hands, nameplate
            var o = p.Office;
            o.SetCashBox(true, 3, false, false, false, false, false, false);
            p.AimAt(o.CashBox.position + Vector3.up * 0.05f);
            yield return Shot("v5s2_cashbox_desk", 1.2f);
            Debug.Log("[SceneTest] hovered " + Rooms.Hovered);
            rooms.SetPovMode(PovMode.CashBox);
            o.SetCashBox(true, 3, false, false, true, false, false, false);
            yield return Shot("v5s2_cashbox_open", 1.4f);
            o.SetCashBox(true, 3, true, false, true, false, false, false);
            o.CashGrab(true);
            yield return Shot("v5s2_cashbox_grab", 0.5f);
            yield return Shot("v5s2_cashbox_taken", 1.0f);
            o.SetCashBox(true, 3, true, false, false, false, false, false);
            rooms.SetPovMode(PovMode.Office);
            p.SetLook(-28f, 30f);
            yield return Shot("v5s2_cashbox_on_desk", 1.2f);
            o.SetCashBox(true, 3, false, true, false, true, false, false);
            p.AimAt(o.CashBox.position + Vector3.up * 0.04f);
            yield return Shot("v5s2_cashbox_witness_lit", 1.0f);
            o.SetCashBox(true, 3, false, false, false, false, false, true);
            yield return Shot("v5s2_cashbox_sealed", 1.0f);
            o.SetCashBox(false, 3, false, false, false, false, false, false);
            p.AimAt(o.Roster.position);
            yield return Shot("v5s2_roster_from_chair", 1.0f);
            Debug.Log("[SceneTest] hovered " + Rooms.Hovered);
            rooms.SetPovMode(PovMode.Roster);
            yield return Shot("v5s2_roster_zoom", 1.4f);
            o.PenStroke(new Vector2(0.18f, 0.58f), new Vector2(0.55f, 0.58f), false, 1.4f);
            yield return Shot("v5s2_roster_writing", 0.9f);
            yield return new WaitForSeconds(1.4f);
            o.PenStroke(new Vector2(0.18f, 0.58f), new Vector2(0.55f, 0.58f), true, 1.4f);
            yield return Shot("v5s2_roster_erasing", 0.9f);
            yield return new WaitForSeconds(1.4f);
            rooms.SetPovMode(PovMode.Office);
            o.SetPrim(6, false);
            o.SetDrawerHeld(true);
            rooms.SetPovMode(PovMode.Drawer);
            yield return Shot("v5s2_drawer_6", 1.5f);
            o.SetPrim(23, true);
            yield return Shot("v5s2_drawer_23", 0.8f);
            o.SetPrim(64, true);
            yield return Shot("v5s2_drawer_64", 0.8f);
            o.SetDrawerHeld(false);
            rooms.SetPovMode(PovMode.Office);
            p.SetLook(75f, 25f);
            yield return Shot("v5s2_look_right_hands", 1.2f);
            p.SetLook(-75f, 20f);
            yield return Shot("v5s2_look_left_hands", 1.0f);
            p.SetLook(30f, 18f);
            yield return Shot("v5s2_look_right30_hands", 1.0f);
            foreach (var nm in new[] { "Mavi", "Yeşil", "Işık" })
            {
                rooms.EnterOffice(0, 0, nm);
                p.AimAt(o.Nameplate.position + Vector3.up * 0.06f);
                yield return Shot("v5s2_nameplate_" + (nm == "Işık" ? "Isik" : nm == "Yeşil" ? "Yesil" : nm), 0.8f);
            }
            rooms.EnterOffice(0, 0, "Deniz");
            p.SetLook(0f, 13f);

            // ---------------------------------------------------------------- meeting
            var seats = new List<MeetingSeatInfo>();
            string[] names = { "Deniz", "Ayşe", "Mert", "Elif", "Kerem", "Zeynep" };
            for (int i = 0; i < 6; i++) seats.Add(new MeetingSeatInfo { Slot = i, ColorIndex = i, Name = names[i], Connected = true });
            rooms.EnterMeeting(2, seats, "paragoz");
            rooms.SetPovMode(PovMode.Meeting);
            rooms.SetTimeOfDay(0.9f);
            p.SetLook(0f, 6f);
            yield return Shot("meeting_seat", 2.0f);
            p.SetLook(-58f, -2f);
            rooms.SetManagerTalking(true);
            yield return Shot("meeting_manager_talking", 1.6f);
            rooms.SetManagerTalking(false);
            p.SetLook(0f, 6f);
            float t0 = Time.time;
            while (Time.time - t0 < 1.6f) { rooms.SetSpeaking(3, 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 7f))); yield return null; }
            yield return Shot("meeting_slot3_talking", 0.0f);
            t0 = Time.time;
            while (Time.time - t0 < 1.2f) { rooms.SetSpeaking(2, 0.8f); yield return null; }
            yield return Shot("meeting_local_talking_all_look", 0.0f);
            rooms.React(1, MeetingReaction.Laugh);
            rooms.React(3, MeetingReaction.Gasp);
            rooms.React(5, MeetingReaction.Cheer);
            rooms.React(0, MeetingReaction.Point);
            rooms.React(4, MeetingReaction.Nod);
            yield return Shot("meeting_reactions", 0.45f);
            rooms.React(5, MeetingReaction.Slump);
            yield return Shot("meeting_slump", 1.0f);
            p.AimAt(SeatHead(p, 1));
            yield return Shot("meeting_hover_seat", 1.0f);
            Debug.Log("[SceneTest] hovered seat " + Rooms.HoveredSeat);
            p.ClickHovered();
            rooms.SetSpotlight(3);
            p.SetLook(0f, 6f);
            t0 = Time.time;
            while (Time.time - t0 < 1.6f) { rooms.SetSpeaking(3, 0.7f); yield return null; }
            yield return Shot("meeting_spotlight_defense", 0.0f);
            rooms.SetSpotlight(-1);
            rooms.SetFired(3);
            p.SetLook(0f, 6f);
            yield return Shot("meeting_fired", 1.4f);
            rooms.SetPovMode(PovMode.Cutscene);
            yield return Shot("meeting_cutscene_fired", 1.4f);
            rooms.SetFired(-1);
            rooms.SetPovMode(PovMode.MeetingProjector);
            yield return Shot("mode_projector", 1.4f);
            rooms.SetPovMode(PovMode.Meeting);
            rooms.EnterMeeting(5, seats, "disiplinci");
            p.SetLook(0f, 6f);
            yield return Shot("meeting_other_side", 1.4f);
            seats.RemoveAt(5); seats.RemoveAt(4);
            rooms.EnterMeeting(1, seats, "kayirmaci");
            p.SetLook(-50f, 0f);
            yield return Shot("meeting_four_players", 1.4f);
            string[] bosses = { "paragoz", "disiplinci", "kayirmaci", "paranoyak" };
            foreach (var b in bosses)
            {
                rooms.EnterMeeting(1, seats, b);
                rooms.SetPovMode(PovMode.Cutscene);
                rooms.SetManagerTalking(true);
                yield return Shot("manager_" + b, 1.3f);
            }
            rooms.SetManagerTalking(false);
            rooms.SetPovMode(PovMode.Meeting);
            rooms.SetTimeOfDay(0.1f);
            p.SetLook(-40f, 2f);
            yield return Shot("meeting_morning_light", 1.4f);

            // ---------------------------------------------------------------- v5 step 3a: gaze, whiteboards, folder, ballot, box
            yield return Step3a(p, rooms, names);

            WriteMemory();
            yield return new WaitForSeconds(0.5f);
            Debug.Log("[SceneTest] done, " + _n + " shots");
            Application.Quit(0);
        }

        IEnumerator Step3a(RoomsPresenter p, IRoomPresenter rooms, string[] names)
        {
            var mr = p.Meeting;
            var six = new List<MeetingSeatInfo>();
            for (int i = 0; i < 6; i++) six.Add(new MeetingSeatInfo { Slot = i, ColorIndex = i, Name = names[i], Connected = true });
            rooms.EnterMeeting(0, six, "paragoz");
            rooms.SetPovMode(PovMode.Meeting);
            rooms.SetTimeOfDay(0.9f);
            p.SetLook(0f, 6f);
            float y, pt;
            for (int i = 1; i < 6; i++) if (mr.GazeAngles(i, 0, out y, out pt)) mr.SetGaze(i, y, pt, 30f);
            yield return Shot("s3a_gaze_everyone_at_you", 1.6f);
            if (mr.GazeAngles(1, 4, out y, out pt)) mr.SetGaze(1, y, pt, 30f);
            if (mr.GazeAngles(3, 5, out y, out pt)) mr.SetGaze(3, y, pt, 30f);
            mr.SetGaze(5, 0f, 34f, 30f); // looking down at the folder
            if (mr.GazeAngles(2, 3, out y, out pt)) mr.SetGaze(2, y, pt, 30f);
            yield return Shot("s3a_gaze_mixed", 1.4f);
            float t0 = Time.time;
            while (Time.time - t0 < 1.4f) { rooms.SetSpeaking(3, 0.55f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 7f))); yield return null; }
            yield return Shot("s3a_talking_squash", 0.0f);
            mr.ClearGaze();
            var board = mr.BoardFor(0);
            if (board != null) p.AimAt(board.bounds.center);
            yield return Shot("s3a_board_from_seat", 1.0f);
            Debug.Log("[SceneTest] hovered meeting " + Rooms.HoveredMeeting);
            rooms.SetPovMode(PovMode.MeetingBoard);
            yield return Shot("s3a_board_zoom", 1.4f);
            rooms.SetPovMode(PovMode.Meeting);
            rooms.EnterMeeting(1, six, "paragoz");
            board = mr.BoardFor(1);
            if (board != null) p.AimAt(board.bounds.center);
            yield return Shot("s3a_board_stand_right_row", 1.4f);
            rooms.SetPovMode(PovMode.MeetingBoard);
            yield return Shot("s3a_board_stand_zoom", 1.4f);
            rooms.SetPovMode(PovMode.Meeting);
            rooms.EnterMeeting(0, six, "paragoz");
            p.SetLook(0f, 6f);
            var seat = mr.Seats[mr.LocalSeat];
            if (seat.Folder != null) p.AimAt(seat.Folder.position + Vector3.up * 0.02f);
            yield return Shot("s3a_folder_on_table", 1.2f);
            Debug.Log("[SceneTest] hovered meeting " + Rooms.HoveredMeeting);
            p.SetLook(0f, 6f);
            mr.SetFolderHeld(true);
            yield return Shot("s3a_folder_lifting", 0.25f);
            yield return Shot("s3a_folder_held", 1.0f);
            mr.SetFolderHeld(false);
            yield return new WaitForSeconds(0.6f);
            mr.SetBallotBox(true, true);
            mr.SetBallot(true);
            yield return new WaitForSeconds(0.8f);
            if (seat.BallotSpot != null) p.AimAt(seat.BallotSpot.position);
            yield return Shot("s3a_ballot_on_table", 0.8f);
            Debug.Log("[SceneTest] hovered meeting " + Rooms.HoveredMeeting);
            p.SetLook(0f, 6f);
            mr.SetBallotHeld(true);
            yield return Shot("s3a_ballot_held", 1.1f);
            mr.DropBallot();
            yield return Shot("s3a_ballot_folding", 0.22f);
            yield return Shot("s3a_ballot_flying", 0.35f);
            yield return new WaitForSeconds(0.8f);
            for (int i = 1; i < 6; i++) { mr.OtherDrop(i); yield return new WaitForSeconds(0.25f); }
            p.AimAt(mr.BallotBox.position + Vector3.up * 0.1f);
            yield return Shot("s3a_box_pile_" + mr.PileCount, 1.6f);
            mr.SetBallot(false);
            mr.SetBallotBox(false);
            yield return Shot("s3a_box_gone", 0.6f);
        }

        static Vector3 SeatHead(RoomsPresenter p, int slot)
        {
            Vector3 head;
            return p.Meeting.TryGetHead(slot, Vector3.zero, out head) ? head : p.Meeting.transform.position + Vector3.up * 1.3f;
        }

        void WriteMemory()
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            proc.Refresh();
            string s = string.Format("peak_working_set_mb={7:0.0}\nworking_set_mb={0:0.0}\nprivate_mb={1:0.0}\nunity_allocated_mb={2:0.0}\nunity_reserved_mb={3:0.0}\nmono_heap_mb={4:0.0}\ngfx_driver_mb={5:0.0}\nshots={6}\n",
                proc.WorkingSet64 / 1048576.0, proc.PrivateMemorySize64 / 1048576.0,
                UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576.0,
                UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576.0,
                UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / 1048576.0,
                UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576.0, _n, proc.PeakWorkingSet64 / 1048576.0);
            File.WriteAllText(Path.Combine(_out, "memory.txt"), s);
            Debug.Log("[SceneTest] memory\n" + s);
        }
    }
}
