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
            Debug.Log("[SceneTest] silhouette active=" + (sil != null && sil.gameObject.activeInHierarchy) + " pos=" + (sil != null ? sil.position.ToString("F2") : "-")
                      + " visible=" + (sil != null && sil.GetComponent<Renderer>().isVisible));
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

            WriteMemory();
            yield return new WaitForSeconds(0.5f);
            Debug.Log("[SceneTest] done, " + _n + " shots");
            Application.Quit(0);
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
