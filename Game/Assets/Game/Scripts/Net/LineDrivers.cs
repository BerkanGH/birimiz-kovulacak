using System.Collections;
using System.Collections.Generic;
using System.IO;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Automated player for a network client (-bot): feeds its own view to the v5 <see cref="LineBot"/> and sends what it decides.</summary>
    public sealed class LineBotDriver : MonoBehaviour
    {
        LineBot _bot;
        int _week = -1;
        float _next;

        void Update()
        {
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            if (v == null || lc.MySlot < 0) return;
            if (_week != v.WeekId || _bot == null)
            {
                _week = v.WeekId;
                _bot = new LineBot(lc.MySlot, LaunchArgs.Current.Seed * 131 + lc.MySlot * 7 + v.WeekId * 1009, LaunchArgs.Current.BotPolicy)
                {
                    ThinkSeconds = HostLineController.BotThinkSeconds,
                };
            }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            if (LaunchArgs.Current.CallTest && CallTest(lc, v)) return;
            LineAction a;
            try { a = _bot.Next(v, lc.Config, lc.Now); }
            catch (System.Exception e) { Debug.LogException(e); return; }
            if (a != null) lc.Send(a.Verb, a.Target, a.A, a.B, a.C, a.Text);
        }

        // ---- -calltest: exercise calls, the recorder and leaks (LineBot itself never calls; Phase 3 adds that).
        double _phaseKey = double.NaN;
        bool _called, _recordAsked, _hungUp, _leakAsked;

        /// <summary>
        /// Test hook for automated runs: in Intro and CrisisCalls the connected players pair up by seat order (1st calls
        /// 2nd, 3rd calls 4th, ...); callees answer; a caller records its first call and leaks it in meeting 1's
        /// discussion. Returns true when it sent something this tick.
        /// </summary>
        bool CallTest(LineClient lc, LineView v)
        {
            if (v.PhaseStartTime != _phaseKey) { _phaseKey = v.PhaseStartTime; _called = _hungUp = _leakAsked = false; }
            var m = v.Mine;
            var ph = m.Phone;
            if (ph.Status == CallStatus.Ringing) { lc.Send(LineVerb.Answer); return true; }
            double now = lc.Now, len = v.PhaseEndTime - v.PhaseStartTime, elapsed = now - v.PhaseStartTime;
            if (v.Phase == LinePhase.Intro || v.Phase == LinePhase.CrisisCalls)
            {
                var order = new List<int>();
                foreach (var p in v.Players) if (p.Connected) order.Add(p.Id);
                order.Sort();
                int idx = order.IndexOf(lc.MySlot);
                bool caller = idx >= 0 && idx % 2 == 0 && idx + 1 < order.Count;
                if (!caller) return false;
                if (ph.Status == CallStatus.Idle && !_called && elapsed > 0.5) { _called = true; lc.Send(LineVerb.Call, order[idx + 1]); return true; }
                if (ph.Status == CallStatus.Connected)
                {
                    if (!_recordAsked && v.Features.RecordingsEnabled && m.RecorderUsesLeft > 0) { _recordAsked = true; lc.Send(LineVerb.Record); return true; }
                    if (!_hungUp && elapsed > len * 0.8) { _hungUp = true; lc.Send(LineVerb.HangUp); return true; }
                }
                return false;
            }
            if (v.Phase == LinePhase.MeetingDiscussion && v.Day == 0 && v.Features.LeakOpen && !_leakAsked)
                foreach (var r in m.Recorded)
                    if (!r.Leaked) { _leakAsked = true; lc.Send(LineVerb.Leak, a: r.CallId); return true; }
            return false;
        }
    }

    /// <summary>
    /// Saves screenshots at key moments of each match (automation: -screenshots). Office phases capture the office
    /// views a player sees: the desk, the computer on the task app, the phone panel (a call is usually running with
    /// -calltest), the cork board and a standing view; step 2: the task's object (the Petty Cash box while choosing and
    /// in its break, the Weekend Shift roster while choosing and at the reveal) and the drawer; meeting phases (step 3a)
    /// capture the meeting room: the seat view, the folder, the whiteboard zoom, the ballot paper and the ballot box.
    /// </summary>
    public sealed class LineScreenshotRecorder : MonoBehaviour
    {
        struct ShotSpec
        {
            public float Delay;
            public string Tag;
            public System.Action Setup;
            public ShotSpec(float delay, string tag, System.Action setup) { Delay = delay; Tag = tag; Setup = setup; }
        }

        void Start() { LineClient.I.PhaseChanged += (from, to) => StartCoroutine(Shot(to)); }

        static OfficeUi Office => OfficeUi.I;
        static RoomsPresenter World => RoomsPresenter.Instance;

        static void Desk(float yaw, float pitch)
        {
            if (Office != null) Office.CloseFocus();
            if (World != null) { World.SitDown(false); World.SetLook(yaw, pitch); }
        }

        static MeetingUi Meeting => MeetingUi.I;

        /// <summary>Meeting room: nothing held, looking around from the seat.</summary>
        static void Seat(float yaw, float pitch)
        {
            if (Meeting != null) Meeting.CloseAll();
            if (World != null) World.SetLook(yaw, pitch);
        }

        /// <summary>Meeting room: the ballot box with the papers dropped so far.</summary>
        static void BoxView()
        {
            if (Meeting != null) Meeting.CloseAll();
            var box = World != null && World.Meeting != null ? World.Meeting.BallotBox : null;
            if (box != null) World.AimAt(box.position + Vector3.up * 0.1f);
        }

        /// <summary>The object that plays the task (step 2): its view, or the computer's task app for Client Bonus.</summary>
        static ShotSpec TaskObject(float delay, TaskKind kind)
        {
            if (kind == TaskKind.PettyCash) return new ShotSpec(delay, "cashbox", () => Office?.OpenObject(OfficeUi.Focus.CashBox));
            if (kind == TaskKind.WeekendShift) return new ShotSpec(delay, "roster", () => Office?.OpenObject(OfficeUi.Focus.Roster));
            return new ShotSpec(delay, "computer", () => Office?.OpenComputer(OfficeComputer.App.Task));
        }

        static ShotSpec[] Plan(LinePhase phase, LineView v)
        {
            var kind = v.Task != null ? v.Task.Kind : TaskKind.None;
            switch (phase)
            {
                case LinePhase.Intro:
                    return new[]
                    {
                        new ShotSpec(2.5f, "desk", () => Desk(0f, 15f)), new ShotSpec(9f, "phone", () => Office?.OpenPhone()),
                        new ShotSpec(15f, "drawer", () => Office?.OpenObject(OfficeUi.Focus.Drawer)),
                    };
                case LinePhase.TaskPlay:
                    return kind == TaskKind.ClientBonus
                        ? new[] { TaskObject(4f, kind), new ShotSpec(20f, "board", () => Office?.OpenBoard()) }
                        : new[] { TaskObject(4f, kind), new ShotSpec(14f, "computer", () => Office?.OpenComputer(OfficeComputer.App.Task)), new ShotSpec(24f, "board", () => Office?.OpenBoard()) };
                case LinePhase.TaskReveal:
                    return new[] { TaskObject(1.5f, kind) };
                case LinePhase.Break:
                    return new[]
                    {
                        kind == TaskKind.PettyCash ? TaskObject(3f, kind) : new ShotSpec(3f, "computer", () => Office?.OpenComputer(OfficeComputer.App.Task)),
                        new ShotSpec(12f, "standing", () => { Office?.CloseFocus(); World?.StandAt(new Vector2(-1.45f, -1.7f), 32f, 12f); }),
                        new ShotSpec(20f, "drawer", () => { World?.SitDown(false); Office?.OpenObject(OfficeUi.Focus.Drawer); }),
                    };
                case LinePhase.CrisisCalls:
                    return new[]
                    {
                        new ShotSpec(6f, "phone", () => Office?.OpenPhone()),
                        new ShotSpec(30f, "desk", () => Desk(24f, 22f)),
                        new ShotSpec(52f, "board", () => Office?.OpenBoard()),
                    };
                // v5 step 3a: the meeting room (seated look, the folder, the whiteboard, the ballot and the box)
                case LinePhase.MeetingDossier: return new[] { new ShotSpec(5f, "room", () => Seat(0f, 6f)) };
                case LinePhase.MeetingDiscussion:
                    return new[]
                    {
                        new ShotSpec(4f, "room", () => Seat(-20f, 4f)), new ShotSpec(10f, "folder", () => Meeting?.OpenFolder(MeetingFolder.Tab.Actions)),
                        new ShotSpec(16f, "board", () => Meeting?.OpenBoard()),
                    };
                case LinePhase.MeetingVote:
                case LinePhase.CrisisVote:
                case LinePhase.FinalVote:
                    return new[] { new ShotSpec(3f, "ballot", () => { Seat(0f, 6f); Meeting?.OpenBallot(); }), new ShotSpec(9f, "box", BoxView) };
                case LinePhase.MeetingVerdict: return new[] { new ShotSpec(3f, "board", () => Meeting?.OpenBoard()) };
                case LinePhase.CrisisVerdict: return new[] { new ShotSpec(3f, "board", () => Meeting?.OpenBoard()) };
                case LinePhase.FinalDiscussion: return new[] { new ShotSpec(5f, "room", () => Seat(15f, 4f)), new ShotSpec(11f, "folder", () => Meeting?.OpenFolder(MeetingFolder.Tab.Chat)) };
                case LinePhase.FinalDefense: return new[] { new ShotSpec(5f, "room", () => Seat(0f, 5f)) };
                case LinePhase.FinalVerdict: return new[] { new ShotSpec(3f, "room", () => Seat(0f, 5f)) };
                case LinePhase.Results: return new[] { new ShotSpec(5f, null, null) };
                default: return null;
            }
        }

        IEnumerator Shot(LinePhase phase)
        {
            var v0 = LineClient.I.View;
            if (v0 == null) yield break;
            var plan = Plan(phase, v0);
            if (plan == null) yield break;
            int week = v0.WeekId, day = v0.Day, task = v0.TaskNumber;
            double phaseStart = v0.PhaseStartTime;
            float start = Time.realtimeSinceStartup;
            float scale = Mathf.Max(0.3f, LaunchArgs.Current.DurationScale);
            foreach (var spec in plan)
            {
                while (Time.realtimeSinceStartup - start < spec.Delay * scale) yield return null;
                var v = LineClient.I.View;
                if (v == null || v.Phase != phase || v.PhaseStartTime != phaseStart) yield break;
                if (spec.Setup != null)
                {
                    try { spec.Setup(); }
                    catch (System.Exception e) { Debug.LogException(e); }
                    // the camera eases to the object, the panel fades in
                    float settle = Time.realtimeSinceStartup + 1.0f;
                    while (Time.realtimeSinceStartup < settle) yield return null;
                    v = LineClient.I.View;
                    if (v == null || v.Phase != phase || v.PhaseStartTime != phaseStart) yield break;
                }
                string dir = Path.GetFullPath(Path.Combine(MatchLog.Directory ?? Application.persistentDataPath, "shots"));
                Directory.CreateDirectory(dir);
                string extra = task >= 0 && LineClient.IsCallsPhase(phase) && phase != LinePhase.Intro && phase != LinePhase.CrisisCalls ? "_t" + task
                    : phase == LinePhase.FinalVote || phase == LinePhase.FinalVerdict ? "_r" + (v.Final != null ? v.Final.Round : 0) + (v.Final != null && v.Final.Revote ? "rv" : "") : "";
                if (!string.IsNullOrEmpty(spec.Tag)) extra += "_" + spec.Tag;
                string file = Path.Combine(dir, string.Format("slot{0}_w{1}_d{2}_{3}{4}_{5:000}.png", LineClient.I.MySlot, week, day, phase, extra, (int)(Time.realtimeSinceStartup - start)));
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(file);
                MatchLog.Write("screenshot " + file);
            }
        }
    }

    /// <summary>
    /// Client side: drives the 3D world (<see cref="Rooms.Presenter"/>) from the local <see cref="LineView"/>. v5: the
    /// calls phases (intro, tasks, breaks, crisis calls) put the player in their office (phone/monitor/recorder states,
    /// the meeting lamp over the door before a meeting, light moving from morning to evening over the three tasks);
    /// meetings, the crisis vote, the final and the results put everyone at the meeting table (talk animation,
    /// defense spotlight, fired players). A place switch happens under a short fade to black
    /// (<see cref="ShownPlace"/> flips while the screen is dark, and UIRoot shows the matching UI).
    /// </summary>
    [DefaultExecutionOrder(-100)] // before UIRoot
    public sealed class LineWorldDriver : MonoBehaviour
    {
        public enum Place { None, Office, Meeting }

        /// <summary>The room on screen right now (changes in the dark middle of a fade).</summary>
        public static Place ShownPlace { get; private set; }

        int _officeKey = -1, _meetingKey = -1, _firedShown = -2;
        bool _switching, _closed, _bound;
        double _boxRound = double.NaN;

        /// <summary>UI mocks: voice level per slot instead of the voice system (talk animation in screenshots).</summary>
        public static System.Func<int, float> MockSpeech;
        readonly List<MeetingSeatInfo> _seats = new List<MeetingSeatInfo>();

        void Update()
        {
            var rooms = Rooms.Presenter;
            var lc = LineClient.I;
            if (rooms == null || lc == null) return;
            if (!_bound) { _bound = true; lc.PhaseChanged += (from, to) => { if (to == LinePhase.None) _closed = true; }; }
            var v = lc.View;
            if (v == null || _closed)
            {
                // the week closed (lobby; a new mock scenario may already have a view in the same frame)
                _closed = false;
                _officeKey = _meetingKey = -1; _firedShown = -2; _boxRound = double.NaN;
                if (ShownPlace != Place.None) { ShownPlace = Place.None; ScreenFade.To(0f, 0f); }
                if (v == null) return;
            }
            var want = LineClient.IsCallsPhase(v.Phase) ? Place.Office
                : LineClient.IsMeetingPhase(v.Phase) || v.Phase == LinePhase.Results ? Place.Meeting : ShownPlace;
            if (want == Place.None) return;
            if (want != ShownPlace)
            {
                if (!_switching) StartCoroutine(Switch(want));
                return;
            }
            if (_switching) return;
            if (want == Place.Office) TickOffice(rooms, lc, v);
            else TickMeeting(rooms, lc, v);
        }

        /// <summary>Fade to black, swap the room (and with it the UI), fade back in.</summary>
        IEnumerator Switch(Place want)
        {
            _switching = true;
            if (ShownPlace == Place.None) ScreenFade.To(1f, 0f);
            else
            {
                ScreenFade.To(1f, 0.28f);
                float until = Time.realtimeSinceStartup + 0.6f;
                while (ScreenFade.Alpha < 0.99f && Time.realtimeSinceStartup < until) yield return null;
            }
            _officeKey = _meetingKey = -1;
            bool fresh = ShownPlace == Place.None;
            ShownPlace = want;
            _switching = false;
            // a new match (or the first office of this session) starts in the chair
            if (fresh && want == Place.Office && RoomsPresenter.Instance != null) RoomsPresenter.Instance.ResetOffice();
            // let the new room enter and the camera snap before the light comes back
            yield return null;
            yield return null;
            ScreenFade.To(0f, 0.45f);
        }

        void TickOffice(IRoomPresenter rooms, LineClient lc, LineView v)
        {
            double now = lc.Now;
            int me = lc.MySlot;
            int key = v.WeekId * 100 + v.Day * 10 + (v.Phase == LinePhase.CrisisCalls ? 5 : 0);
            if (_officeKey != key)
            {
                _officeKey = key;
                _meetingKey = -1;
                var p = lc.Me;
                rooms.EnterOffice(me, p != null ? p.ColorIndex : me, p != null ? p.Name : "");
                rooms.SetPovMode(PovMode.Office);
            }
            rooms.SetPhoneState(PhoneState(v));
            rooms.SetMonitorState(MonitorStateOf(v, now));
            rooms.SetTimeOfDay(TimeOfDay(v, now));
            var office = RoomsPresenter.Instance != null ? RoomsPresenter.Instance.Office : null;
            if (office != null)
            {
                var m = v.Mine;
                office.SetRecorder(v.Features.RecordingsEnabled ? Mathf.Clamp(m.RecorderUsesLeft, 0, 3) : 0, PlayBlocks.RecordingCurrentCall(v), m.Recorded.Count > 0);
                office.SetDoorLamp(MeetingSoon(v, now));
            }
        }

        /// <summary>The next phase is in the meeting room and it starts within 15 s (the lamp over the door lights up).</summary>
        static bool MeetingSoon(LineView v, double now)
        {
            bool next = v.Phase == LinePhase.CrisisCalls || (v.Phase == LinePhase.Break && v.TaskNumber == 1);
            return next && v.PhaseEndTime - now < 15.0;
        }

        void TickMeeting(IRoomPresenter rooms, LineClient lc, LineView v)
        {
            int me = lc.MySlot;
            int key = v.WeekId * 100 + v.Day * 10 + (v.Phase == LinePhase.CrisisCalls ? 5 : 0);
            if (_meetingKey != key)
            {
                _meetingKey = key;
                _officeKey = -1;
                _firedShown = -2;
                _seats.Clear();
                foreach (var p in v.Players) _seats.Add(new MeetingSeatInfo { Slot = p.Id, ColorIndex = p.ColorIndex, Name = p.Name, Connected = p.Connected });
                rooms.EnterMeeting(me, _seats, "");
                rooms.SetPovMode(PovMode.Meeting);
            }
            var voice = VoiceSystem.Instance;
            foreach (var p in v.Players)
                rooms.SetSpeaking(p.Id, MockSpeech != null ? MockSpeech(p.Id) : voice != null ? voice.GetSpeechLevel(p.Id) : 0f);
            rooms.SetSpotlight(v.Phase == LinePhase.FinalDefense && v.Final != null ? v.Final.Defender : -1);
            // v5 step 3a: shared gaze (send mine, turn the colleagues' heads) and the ballot box for the votes
            var room = RoomsPresenter.Instance != null ? RoomsPresenter.Instance.Meeting : null;
            if (room != null)
            {
                MeetingGaze.TickLocal(lc, v);
                foreach (var p in v.Players)
                {
                    float gy, gp;
                    if (p.Id != me && MeetingGaze.TryGet(p.Id, out gy, out gp)) room.SetGaze(p.Id, gy, gp, 0.4f);
                }
                bool box = v.Phase == LinePhase.MeetingVote || v.Phase == LinePhase.CrisisVote || v.Phase == LinePhase.FinalVote
                           || v.Phase == LinePhase.MeetingVerdict || v.Phase == LinePhase.CrisisVerdict;
                // a new vote (also the final's revote / second round) starts with an empty box
                bool fresh = box && v.PhaseStartTime != _boxRound && (v.Phase == LinePhase.MeetingVote || v.Phase == LinePhase.CrisisVote || v.Phase == LinePhase.FinalVote);
                if (fresh) _boxRound = v.PhaseStartTime;
                room.SetBallotBox(box, fresh);
            }
            // The room shows one fired avatar: the latest firing.
            int fired = -1;
            if (v.Final != null && v.Final.Firings.Count > 0) fired = v.Final.Firings[v.Final.Firings.Count - 1].Player;
            if (fired != _firedShown) { _firedShown = fired; rooms.SetFired(fired); }
            rooms.SetTimeOfDay(v.Day >= 2 ? 1f : 0.9f);
        }

        static PhoneLineState PhoneState(LineView v)
        {
            switch (v.Mine.Phone.Status)
            {
                case CallStatus.Ringing: return PhoneLineState.Ringing;
                case CallStatus.Dialing: return PhoneLineState.Dialing;
                case CallStatus.Connected: return PhoneLineState.InCall;
                default: return OfficeUi.I != null && OfficeUi.I.PhoneOpen ? PhoneLineState.OffHook : PhoneLineState.Idle;
            }
        }

        static MonitorState MonitorStateOf(LineView v, double now)
        {
            if (v.Phase == LinePhase.TaskPlay && v.Task != null && !v.Task.Submitted) return MonitorState.Alert;
            // New texts blink the monitor for a few seconds (messages arrive outside the view).
            var msgs = LineClient.I != null ? LineClient.I.Messages : null;
            if (msgs != null && msgs.Count > 0 && msgs[msgs.Count - 1].Time > now - 8.0 && msgs[msgs.Count - 1].From != v.Me) return MonitorState.Alert;
            return MonitorState.Normal;
        }

        /// <summary>Morning light at the intro, evening by the crisis calls (three tasks in between).</summary>
        static float TimeOfDay(LineView v, double now)
        {
            if (v.Phase == LinePhase.Intro) return 0.05f;
            if (v.Phase == LinePhase.CrisisCalls) return 0.85f;
            float within = 0f;
            double len = v.PhaseEndTime - v.PhaseStartTime;
            if (len > 0.01) within = Mathf.Clamp01((float)((now - v.PhaseStartTime) / len));
            float stage = v.Phase == LinePhase.TaskPlay ? within * 0.8f : 1f;
            float t = Mathf.Max(0, v.TaskNumber) + stage;
            return 0.12f + 0.66f * Mathf.Clamp01(t / LineTasks.Count);
        }
    }
}
