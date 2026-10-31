using System.Collections;
using System.Collections.Generic;
using System.IO;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>Automated player for a network client (-bot): feeds its own view to <see cref="LineBot"/> and sends what it decides.</summary>
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
                _bot = new LineBot(lc.MySlot, LaunchArgs.Current.Seed * 131 + lc.MySlot * 7 + v.WeekId * 1009, LineBotPolicy.Talk);
            }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            LineAction a;
            try { a = _bot.Next(v, lc.Config, lc.Now); }
            catch (System.Exception e) { Debug.LogException(e); return; }
            if (a != null) lc.Send(a.Verb, a.Target, a.A, a.B, a.C, a.Text);
        }
    }

    /// <summary>Saves screenshots at key moments of each week (automation: -screenshots).</summary>
    public sealed class LineScreenshotRecorder : MonoBehaviour
    {
        void Start() { LineClient.I.PhaseChanged += (from, to) => StartCoroutine(Shot(to)); }

        IEnumerator Shot(LinePhase phase)
        {
            float[] delays;
            switch (phase)
            {
                case LinePhase.Morning: delays = new[] { 4f }; break;
                case LinePhase.RoundIntro: delays = new[] { 3f }; break;
                case LinePhase.RoundPlay: delays = new[] { 20f, 70f }; break;
                case LinePhase.RoundReveal: delays = new[] { 4f }; break;
                case LinePhase.Break: delays = new[] { 5f }; break;
                case LinePhase.MeetingDossier: delays = new[] { 6f }; break;
                case LinePhase.MeetingDiscussion: delays = new[] { 10f }; break;
                case LinePhase.MeetingVote: delays = new[] { 8f }; break;
                case LinePhase.MeetingVerdict: delays = new[] { 3f }; break;
                case LinePhase.FinalDefense: delays = new[] { 8f }; break;
                case LinePhase.FinalVote: delays = new[] { 8f }; break;
                case LinePhase.FinalVerdict: delays = new[] { 5f }; break;
                case LinePhase.WeekResults: delays = new[] { 5f }; break;
                default: yield break;
            }
            var v0 = LineClient.I.View;
            if (v0 == null) yield break;
            int week = v0.WeekId, day = v0.Day, round = v0.RoundIndex;
            float start = Time.realtimeSinceStartup;
            foreach (float d in delays)
            {
                while (Time.realtimeSinceStartup - start < d * Mathf.Max(0.3f, LaunchArgs.Current.DurationScale)) yield return null;
                var v = LineClient.I.View;
                if (v == null || v.Phase != phase || v.Day != day) yield break;
                string dir = Path.GetFullPath(Path.Combine(MatchLog.Directory ?? Application.persistentDataPath, "shots"));
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, string.Format("slot{0}_w{1}_d{2}_{3}{4}_{5:000}.png", LineClient.I.MySlot, week, day, phase,
                    round >= 0 && LineClient.IsOfficePhase(phase) && phase != LinePhase.Morning ? "_r" + round : "", (int)(Time.realtimeSinceStartup - start)));
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(file);
                MatchLog.Write("screenshot " + file);
            }
        }
    }

    /// <summary>
    /// Client side: drives the 3D world (<see cref="Rooms.Presenter"/>) from the local <see cref="LineView"/> —
    /// which room, phone/monitor states, light of the day, meeting seats, talk animation, spotlight, firing.
    /// UI owns the camera focus modes (computer/phone, meeting screen view) and the meeting cues; this only resets the
    /// camera mode when the office day starts.
    /// </summary>
    [DefaultExecutionOrder(-100)] // before UIRoot: EnterMeeting resets cues that MeetingUi re-applies the same frame
    public sealed class LineWorldDriver : MonoBehaviour
    {
        int _officeKey = -1, _meetingKey = -1;
        readonly List<MeetingSeatInfo> _seats = new List<MeetingSeatInfo>();

        void Update()
        {
            var rooms = Rooms.Presenter;
            var lc = LineClient.I;
            if (rooms == null || lc == null) return;
            var v = lc.View;
            if (v == null) { _officeKey = _meetingKey = -1; return; }
            double now = lc.Now;
            int me = lc.MySlot;
            int key = v.WeekId * 100 + v.Day;
            if (LineClient.IsOfficePhase(v.Phase))
            {
                if (_officeKey != key)
                {
                    _officeKey = key;
                    _meetingKey = -1;
                    var p = lc.Me;
                    rooms.EnterOffice(me, p != null ? p.ColorIndex : me, p != null ? p.Name : "");
                    if (rooms.Mode != PovMode.Monitor && rooms.Mode != PovMode.Phone) rooms.SetPovMode(PovMode.Office);
                }
                // Phone/monitor prop states are driven by DeskUi (DrivesWorldProps) unless it is switched off.
                if (!DeskUi.DrivesWorldProps)
                {
                    rooms.SetPhoneState(PhoneState(v, now));
                    rooms.SetMonitorState(MonitorStateOf(v, now));
                }
                rooms.SetTimeOfDay(TimeOfDay(v, now));
                return;
            }
            bool meetingPlace = LineClient.IsMeetingPhase(v.Phase) || v.Phase == LinePhase.WeekResults;
            if (!meetingPlace) return;
            if (_meetingKey != key)
            {
                _meetingKey = key;
                _officeKey = -1;
                _seats.Clear();
                foreach (var p in v.Players) _seats.Add(new MeetingSeatInfo { Slot = p.Id, ColorIndex = p.ColorIndex, Name = p.Name, Connected = p.Connected });
                rooms.EnterMeeting(me, _seats, v.ManagerProfileId);
            }
            // Camera mode, spotlight, firing pose and the manager's talk belong to MeetingUi; this only drives the talk animation and light.
            var voice = VoiceSystem.Instance;
            foreach (var p in v.Players)
                rooms.SetSpeaking(p.Id, voice != null ? voice.GetSpeechLevel(p.Id) : 0f);
            rooms.SetTimeOfDay(v.Features.IsLastDay ? 1f : 0.9f);
        }

        static PhoneLineState PhoneState(LineView v, double now)
        {
            if (v.Mine.PhoneDeadUntil > now) return PhoneLineState.Dead;
            switch (v.Mine.Phone.Status)
            {
                case CallStatus.Ringing: return PhoneLineState.Ringing;
                case CallStatus.Dialing: return PhoneLineState.Dialing;
                case CallStatus.Connected: return PhoneLineState.InCall;
                default: return PhoneLineState.Idle;
            }
        }

        static MonitorState MonitorStateOf(LineView v, double now)
        {
            if (v.Mine.ComputerBlockedUntil > now) return v.Mine.ComputerBlockedBy == TrickType.KahveDok ? MonitorState.Coffee : MonitorState.Offline;
            if (v.Phase == LinePhase.RoundIntro) return MonitorState.Alert;
            // New mail/texts blink the monitor for a few seconds (messages arrive outside the view).
            var msgs = LineClient.I != null ? LineClient.I.Messages : null;
            if (msgs != null && msgs.Count > 0 && msgs[msgs.Count - 1].Time > now - 8.0 && msgs[msgs.Count - 1].From != v.Me) return MonitorState.Alert;
            return MonitorState.Normal;
        }

        /// <summary>Morning light at the start of the day, evening at the meeting.</summary>
        static float TimeOfDay(LineView v, double now)
        {
            if (v.Phase == LinePhase.Morning) return 0.05f;
            int rounds = Mathf.Max(1, v.TodayTasks.Count);
            float within = 0f;
            double len = v.PhaseEndTime - v.PhaseStartTime;
            if (len > 0.01) within = Mathf.Clamp01((float)((now - v.PhaseStartTime) / len));
            float stage = v.Phase == LinePhase.RoundIntro ? 0f : v.Phase == LinePhase.RoundPlay ? within : 1f;
            float r = Mathf.Max(0, v.RoundIndex) + stage;
            return 0.12f + 0.66f * Mathf.Clamp01(r / rounds);
        }
    }
}
