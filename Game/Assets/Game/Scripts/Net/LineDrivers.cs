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
            LineAction a;
            try { a = _bot.Next(v, lc.Config, lc.Now); }
            catch (System.Exception e) { Debug.LogException(e); return; }
            if (a != null) lc.Send(a.Verb, a.Target, a.A, a.B, a.C, a.Text);
        }
    }

    /// <summary>Saves screenshots at key moments of each match (automation: -screenshots).</summary>
    public sealed class LineScreenshotRecorder : MonoBehaviour
    {
        void Start() { LineClient.I.PhaseChanged += (from, to) => StartCoroutine(Shot(to)); }

        IEnumerator Shot(LinePhase phase)
        {
            float[] delays;
            switch (phase)
            {
                case LinePhase.Intro: delays = new[] { 4f }; break;
                case LinePhase.TaskPlay: delays = new[] { 6f }; break;
                case LinePhase.TaskReveal: delays = new[] { 3f }; break;
                case LinePhase.Break: delays = new[] { 6f }; break;
                case LinePhase.MeetingDossier: delays = new[] { 6f }; break;
                case LinePhase.MeetingDiscussion: delays = new[] { 10f }; break;
                case LinePhase.MeetingVote: delays = new[] { 8f }; break;
                case LinePhase.MeetingVerdict: delays = new[] { 3f }; break;
                case LinePhase.CrisisCalls: delays = new[] { 10f }; break;
                case LinePhase.CrisisVote: delays = new[] { 8f }; break;
                case LinePhase.CrisisVerdict: delays = new[] { 3f }; break;
                case LinePhase.FinalDiscussion: delays = new[] { 8f }; break;
                case LinePhase.FinalDefense: delays = new[] { 6f }; break;
                case LinePhase.FinalVote: delays = new[] { 8f }; break;
                case LinePhase.FinalVerdict: delays = new[] { 4f }; break;
                case LinePhase.Results: delays = new[] { 5f }; break;
                default: yield break;
            }
            var v0 = LineClient.I.View;
            if (v0 == null) yield break;
            int week = v0.WeekId, day = v0.Day, task = v0.TaskNumber;
            double phaseStart = v0.PhaseStartTime;
            float start = Time.realtimeSinceStartup;
            foreach (float d in delays)
            {
                while (Time.realtimeSinceStartup - start < d * Mathf.Max(0.3f, LaunchArgs.Current.DurationScale)) yield return null;
                var v = LineClient.I.View;
                if (v == null || v.Phase != phase || v.PhaseStartTime != phaseStart) yield break;
                string dir = Path.GetFullPath(Path.Combine(MatchLog.Directory ?? Application.persistentDataPath, "shots"));
                Directory.CreateDirectory(dir);
                string extra = task >= 0 && LineClient.IsCallsPhase(phase) && phase != LinePhase.Intro && phase != LinePhase.CrisisCalls ? "_t" + task
                    : phase == LinePhase.FinalVote || phase == LinePhase.FinalVerdict ? "_r" + (v.Final != null ? v.Final.Round : 0) + (v.Final != null && v.Final.Revote ? "rv" : "") : "";
                string file = Path.Combine(dir, string.Format("slot{0}_w{1}_d{2}_{3}{4}_{5:000}.png", LineClient.I.MySlot, week, day, phase, extra, (int)(Time.realtimeSinceStartup - start)));
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(file);
                MatchLog.Write("screenshot " + file);
            }
        }
    }

    /// <summary>
    /// Client side: drives the 3D world (<see cref="Rooms.Presenter"/>) from the local <see cref="LineView"/>. v5: the
    /// calls phases (intro, tasks, breaks, crisis calls) put the player at the office desk (phone/monitor states, light
    /// moving from morning to evening over the three tasks); meetings, the crisis vote, the final and the results put
    /// everyone at the meeting table (talk animation, defense spotlight, fired players).
    /// </summary>
    [DefaultExecutionOrder(-100)] // before UIRoot
    public sealed class LineWorldDriver : MonoBehaviour
    {
        int _officeKey = -1, _meetingKey = -1, _firedShown = -2;
        readonly List<MeetingSeatInfo> _seats = new List<MeetingSeatInfo>();

        void Update()
        {
            var rooms = Rooms.Presenter;
            var lc = LineClient.I;
            if (rooms == null || lc == null) return;
            var v = lc.View;
            if (v == null) { _officeKey = _meetingKey = -1; _firedShown = -2; return; }
            double now = lc.Now;
            int me = lc.MySlot;
            int key = v.WeekId * 100 + v.Day * 10 + (v.Phase == LinePhase.CrisisCalls ? 5 : 0);
            if (LineClient.IsCallsPhase(v.Phase))
            {
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
                return;
            }
            bool meetingPlace = LineClient.IsMeetingPhase(v.Phase) || v.Phase == LinePhase.Results;
            if (!meetingPlace) return;
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
                rooms.SetSpeaking(p.Id, voice != null ? voice.GetSpeechLevel(p.Id) : 0f);
            rooms.SetSpotlight(v.Phase == LinePhase.FinalDefense && v.Final != null ? v.Final.Defender : -1);
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
                default: return PhoneLineState.Idle;
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
