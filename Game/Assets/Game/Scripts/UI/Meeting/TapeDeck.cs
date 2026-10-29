using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// What the voice track tells the meeting UI about the call recording that is playing (level meter, who is talking).
    /// Implemented by the voice track on every client; until it is set the UI fakes both from the schedule in
    /// <see cref="LineMeetingView.Recordings"/>. Must be allocation-free (read every frame).
    /// </summary>
    public interface IRecordingPlayback
    {
        /// <summary>Index of the pick (<see cref="LineRecordingPick.Index"/>) audible right now, -1 when the tape is silent.</summary>
        int PlayingPick { get; }
        /// <summary>Output level of the snippet right now, 0..1.</summary>
        float Level { get; }
        /// <summary>Slot whose voice is loudest in the snippet right now (caller or callee), -1 for nobody/silence.</summary>
        int Speaker { get; }
        /// <summary>Progress through the snippet, 0..1.</summary>
        float Progress { get; }
    }

    public static class RecordingPlayback
    {
        static IRecordingPlayback _source;
        static readonly VoiceTapePlayback _voice = new VoiceTapePlayback();

        /// <summary>
        /// What drives the tape deck. Defaults to the voice system's meeting tape when it runs; set it to override
        /// (tests). While nothing plays the UI animates a plausible fake from the schedule.
        /// </summary>
        public static IRecordingPlayback Source
        {
            get { return _source ?? (VoiceSystem.Instance != null ? _voice : null); }
            set { _source = value; }
        }
    }

    /// <summary>Adapter over <see cref="VoiceSystem"/>'s meeting tape (RecordingActive/Level/Speaker/Progress). Allocation-free.</summary>
    public sealed class VoiceTapePlayback : IRecordingPlayback
    {
        public int PlayingPick
        {
            get
            {
                var vs = VoiceSystem.Instance;
                var lc = LineClient.I;
                if (vs == null || !vs.RecordingActive || lc == null || lc.View == null || lc.View.Meeting == null) return -1;
                var recs = lc.View.Meeting.Recordings;
                int call = vs.CurrentRecording.CallId;
                for (int i = 0; i < recs.Count; i++)
                {
                    var p = recs[i];
                    if (call >= 0 && (p.ChosenCallId == call || p.CandidateCallIds.Contains(call))) return p.Index;
                }
                // call id not known yet: the pick whose slot is now
                double now = lc.Now;
                for (int i = 0; i < recs.Count; i++)
                    if (now >= recs[i].PlayAt - 0.5 && now < recs[i].PlayAt + recs[i].Seconds + 1.0) return recs[i].Index;
                return -1;
            }
        }

        public float Level { get { var vs = VoiceSystem.Instance; return vs != null ? vs.RecordingLevel : 0f; } }
        public int Speaker { get { var vs = VoiceSystem.Instance; return vs != null ? vs.RecordingSpeaker : -1; } }
        public float Progress { get { var vs = VoiceSystem.Instance; return vs != null ? vs.RecordingProgress : 0f; } }
    }

    /// <summary>
    /// Cassette deck with spinning reels, a cassette label "Caller → Callee · 14:32", a waveform that fills while the
    /// 7-second snippet plays, the two speakers' badges (the talking one lights up) and the ON RECORD stamp.
    /// Uses meeting_v3/tape_player + tape_reel when the art exists, USS shapes otherwise.
    /// </summary>
    public sealed class TapeDeck
    {
        const int Bars = 56;
        public readonly VisualElement Root;
        readonly VisualElement _deck, _reelL, _reelR, _packL, _packR, _wave, _playhead, _stamp, _recDot, _callerBadge, _calleeBadge;
        readonly VisualElement[] _bars = new VisualElement[Bars];
        readonly float[] _levels = new float[Bars];
        readonly VisualElement[] _leds = new VisualElement[5];
        int _lit = -1;
        readonly Label _callerName, _calleeName, _clock, _status, _calleeArrow;
        readonly bool _art;
        LineRecordingPick _pick;
        int _shownIndex = int.MinValue, _shownCall = int.MinValue, _shownCaller = int.MinValue;
        int _lastBar = -1, _lastSpeaker = int.MinValue, _lastStatusSec = int.MinValue, _statusState = -1;
        float _angle;
        bool _stamped;

        public TapeDeck(VisualElement parent)
        {
            Root = Box("tape").NoPick().AddTo(parent);
            var row = Box("tape-row").NoPick().AddTo(Root);

            var left = Box("tape-person").NoPick().AddTo(row);
            _callerBadge = Badge(-1, "badge-xl").AddTo(left);
            _callerName = Title("", "tape-person-name").NoPick().AddTo(left);
            Bold(Loc.T("rec.caller"), "tape-person-role").NoPick().AddTo(left);

            var center = Box("tape-center").NoPick().AddTo(row);
            // cassette label as a strip of masking tape above the deck: "Caller → Callee" + "Tuesday 14:32"
            var label = Box("tape-label").NoPick().AddTo(center);
            var lrow = Box("tape-label-row").NoPick().AddTo(label);
            _labelCaller = Hand("", "tape-label-text").NoPick().AddTo(lrow);
            _calleeArrow = Title("→", "tape-label-arrow").NoPick().AddTo(lrow);
            _labelCallee = Hand("", "tape-label-text").NoPick().AddTo(lrow);
            _clock = Memo("", "tape-label-clock").NoPick().AddTo(label);

            _deck = Box("tape-deck").NoPick().AddTo(center);
            _art = UiArt.Apply(_deck, "meeting_v3/tape_player");
            _deck.EnableInClassList("tape-deck-fallback", !_art);
            if (_art)
            {
                // tape_player.png (1024×512): reels are drawn 100×100 centred at (262,214) and (497,214); the record
                // button sits at about (882,389).
                _packL = Box("tape-pack").NoPick();
                _packR = Box("tape-pack").NoPick();
                _reelL = ArtReel(_deck, 262f, 214f);
                _reelR = ArtReel(_deck, 497f, 214f);
                _recDot = Box("tape-rec", "tape-rec-art").NoPick().AddTo(_deck);
                // the deck's 5-segment level meter (x 724..757, rows from the top) lights up with the snippet level
                float[] tops = { 165f, 184f, 208f, 231f, 256f };
                for (int i = 0; i < 5; i++)
                {
                    var led = Box("tape-led").NoPick().AddTo(_deck);
                    led.style.left = Length.Percent(724f / 1024f * 100f);
                    led.style.width = Length.Percent(33f / 1024f * 100f);
                    led.style.top = Length.Percent(tops[i] / 512f * 100f);
                    led.style.height = Length.Percent(13f / 512f * 100f);
                    if (i == 0) led.AddToClassList("tape-led-peak");
                    _leds[4 - i] = led; // _leds[0] = bottom segment
                }
            }
            else
            {
                var cassette = Box("tape-cassette").NoPick().AddTo(_deck);
                var window = Box("tape-window").NoPick().AddTo(cassette);
                _packL = Box("tape-pack").NoPick().AddTo(Box("tape-reel-slot").NoPick().AddTo(window));
                _reelL = Reel(_packL.parent);
                Box("tape-ribbon").NoPick().AddTo(window);
                _packR = Box("tape-pack").NoPick().AddTo(Box("tape-reel-slot").NoPick().AddTo(window));
                _reelR = Reel(_packR.parent);
                _recDot = Box("tape-rec").NoPick().AddTo(_deck);
            }

            var right = Box("tape-person").NoPick().AddTo(row);
            _calleeBadge = Badge(-1, "badge-xl").AddTo(right);
            _calleeName = Title("", "tape-person-name").NoPick().AddTo(right);
            Bold(Loc.T("rec.callee"), "tape-person-role").NoPick().AddTo(right);

            var waveRow = Box("tape-wave-row").NoPick().AddTo(Root);
            _wave = Box("tape-wave").NoPick().AddTo(waveRow);
            for (int i = 0; i < Bars; i++) _bars[i] = Box("tape-bar").NoPick().AddTo(_wave);
            _playhead = Box("tape-playhead").NoPick().AddTo(_wave);
            _status = Bold("", "tape-status").NoPick().AddTo(Root);

            _stamp = Box("tape-stamp").NoPick().AddTo(_deck);
            if (!MeetingText.ApplyStamp(_stamp, "stamps_v3/stamp_kayitta", "stamps_v3/stamp_on_record"))
            {
                _stamp.AddToClassList("stamp-text");
                StampText(Loc.T("rec.stamp"), "stamp-text-label").NoPick().AddTo(_stamp);
            }
            _stamp.style.opacity = 0f;
        }

        readonly Label _labelCaller, _labelCallee;

        static VisualElement ArtReel(VisualElement deck, float cx, float cy)
        {
            var reel = Box("tape-reel", "tape-reel-art", "tape-reel-abs").NoPick().AddTo(deck);
            UiArt.Apply(reel, "meeting_v3/tape_reel");
            reel.style.left = Length.Percent((cx - 50f) / 1024f * 100f);
            reel.style.top = Length.Percent((cy - 50f) / 512f * 100f);
            reel.style.width = Length.Percent(100f / 1024f * 100f);
            reel.style.height = Length.Percent(100f / 512f * 100f);
            return reel;
        }

        VisualElement Reel(VisualElement slot)
        {
            var reel = Box("tape-reel").NoPick().AddTo(slot);
            Box("tape-hub").NoPick().AddTo(reel);
            for (int i = 0; i < 3; i++)
            {
                var s = Box("tape-spoke").NoPick().AddTo(reel);
                s.style.rotate = new Rotate(new Angle(i * 60f, AngleUnit.Degree));
            }
            return reel;
        }

        /// <summary>Shows a pick (null = an idle deck). Cheap when the pick did not change.</summary>
        public void Set(LineRecordingPick pick, LineView v)
        {
            _pick = pick;
            int idx = pick != null ? pick.Index : -2, call = pick != null ? pick.ChosenCallId : -2, who = pick != null ? pick.Caller : -2;
            if (idx == _shownIndex && call == _shownCall && who == _shownCaller) return;
            bool newPick = idx != _shownIndex || call != _shownCall;
            _shownIndex = idx; _shownCall = call; _shownCaller = who;
            int caller = pick != null ? pick.Caller : LineIds.None, callee = pick != null ? pick.Callee : LineIds.None;
            bool known = caller >= 0 && callee >= 0;
            SetBadge(_callerBadge, caller, known ? MeetingText.Name(caller) : "?");
            SetBadge(_calleeBadge, callee, known ? MeetingText.Name(callee) : "?");
            _callerName.text = known ? MeetingText.Name(caller) : "?";
            _calleeName.text = known ? MeetingText.Name(callee) : "?";
            _labelCaller.text = known ? MeetingText.Name(caller) : Loc.T("rec.unknown");
            _labelCallee.text = known ? MeetingText.Name(callee) : "";
            _calleeArrow.Show(known);
            _clock.text = known && pick.CallStart >= 0 ? Loc.F("rec.label_time", MeetingText.Day(v != null ? v.Day : 0), MeetingText.OfficeClock(pick.CallStart, v)) : Loc.T("rec.label_blank");
            if (newPick)
            {
                _stamped = false;
                _stamp.style.opacity = 0f;
                _lastBar = -1;
                for (int i = 0; i < Bars; i++) { _levels[i] = 0f; SetBar(i, 0f, false); }
            }
            _statusState = -1;
        }

        /// <summary>Animates reels, waveform and the speaker highlight. Allocation-free.</summary>
        public void Tick(double now, float dt)
        {
            if (Root.panel == null) return;
            var pick = _pick;
            float progress = 0f, level = 0f;
            int speaker = LineIds.None;
            bool playing = false, done = false;
            if (pick != null)
            {
                var src = RecordingPlayback.Source;
                double t = now - pick.PlayAt;
                float secs = pick.Seconds > 0.1f ? pick.Seconds : 7f;
                if (src != null && src.PlayingPick == pick.Index)
                {
                    playing = true;
                    progress = Mathf.Clamp01(src.Progress);
                    level = Mathf.Clamp01(src.Level);
                    speaker = src.Speaker;
                }
                else if (t >= 0 && t < secs && pick.Caller >= 0)
                {
                    playing = true;
                    progress = (float)(t / secs);
                    FakeVoice(pick, (float)t, out level, out speaker);
                }
                else if (t >= secs || (pick.Played && pick.Caller < 0 && t >= 0)) { done = true; progress = 1f; }
            }

            // reels + tape packs
            if (playing) _angle = (_angle + dt * 210f) % 360f;
            var rot = new Rotate(new Angle(_angle, AngleUnit.Degree));
            _reelL.style.rotate = rot;
            _reelR.style.rotate = new Rotate(new Angle(_angle * 1.18f, AngleUnit.Degree));
            float packL = Mathf.Lerp(96f, 58f, progress), packR = Mathf.Lerp(58f, 96f, progress);
            _packL.style.width = _packL.style.height = Length.Percent(packL);
            _packR.style.width = _packR.style.height = Length.Percent(packR);
            _recDot.EnableInClassList("tape-rec-on", playing && ((int)(now * 2.0) & 1) == 0);

            int lit = playing ? Mathf.Clamp(Mathf.CeilToInt(level * 5f), 0, 5) : 0;
            if (lit != _lit && _leds[0] != null)
            {
                _lit = lit;
                for (int i = 0; i < 5; i++) _leds[i].EnableInClassList("tape-led-on", i < lit);
            }

            // waveform: bars up to the playhead show the level heard at that moment
            int bar = Mathf.Clamp(Mathf.FloorToInt(progress * Bars), 0, Bars - 1);
            if (playing)
            {
                if (bar != _lastBar)
                {
                    for (int i = _lastBar + 1; i <= bar; i++) { _levels[i] = Mathf.Max(0.08f, level); SetBar(i, _levels[i], true); }
                    _lastBar = bar;
                }
                else if (level > _levels[bar]) { _levels[bar] = level; SetBar(bar, level, true); }
            }
            else if (done && _lastBar < Bars - 1 && pick != null && pick.Caller >= 0)
            {
                // Late join / fast forward: fill the rest with the fake shape so the finished tape looks complete.
                for (int i = _lastBar + 1; i < Bars; i++) { _levels[i] = FakeShape(pick, i); SetBar(i, _levels[i], true); }
                _lastBar = Bars - 1;
            }
            _playhead.style.left = Length.Percent(progress * 100f);
            _playhead.EnableInClassList("tape-playhead-on", playing);

            // speaker highlight
            if (speaker != _lastSpeaker)
            {
                _lastSpeaker = speaker;
                bool c = pick != null && speaker == pick.Caller && speaker >= 0, e = pick != null && speaker == pick.Callee && speaker >= 0;
                _callerBadge.EnableInClassList("tape-talking", c);
                _calleeBadge.EnableInClassList("tape-talking", e);
                _callerBadge.parent.EnableInClassList("tape-person-quiet", playing && !c);
                _calleeBadge.parent.EnableInClassList("tape-person-quiet", playing && !e);
            }

            // ON RECORD stamp slams when the tape starts
            if ((playing || done) && pick != null && pick.Caller >= 0 && !_stamped)
            {
                _stamped = true;
                UiMotion.Stamp(_stamp, -9f, 0.24f, 0.15f, () => Sfx.Play("place", 0.6f));
            }

            // status line
            int state = pick == null ? 0 : playing ? 1 : done ? (pick.Caller >= 0 ? 2 : 3) : 4;
            int sec = state == 1 ? Mathf.FloorToInt(progress * (pick.Seconds > 0.1f ? pick.Seconds : 7f)) : state == 4 ? Mathf.CeilToInt((float)(pick.PlayAt - now)) : 0;
            if (state != _statusState || sec != _lastStatusSec)
            {
                _statusState = state;
                _lastStatusSec = sec;
                switch (state)
                {
                    case 0: _status.text = Loc.T("rec.none"); break;
                    case 1: _status.text = Loc.F("rec.playing", ClockInt(sec), ClockInt(Mathf.RoundToInt(pick.Seconds > 0.1f ? pick.Seconds : 7f))); break;
                    case 2: _status.text = Loc.T("rec.done"); break;
                    case 3: _status.text = Loc.T("rec.silent"); break;
                    default: _status.text = sec > 0 ? Loc.F("rec.starts_in", sec) : Loc.T("rec.searching"); break;
                }
                _status.EnableInClassList("tape-status-live", state == 1);
            }
        }

        void SetBar(int i, float level, bool played)
        {
            var b = _bars[i];
            b.style.height = Length.Percent(played ? Mathf.Lerp(10f, 100f, Mathf.Clamp01(level)) : 6f);
            b.EnableInClassList("tape-bar-on", played);
        }

        /// <summary>Plausible conversation: syllable bursts, short pauses, the two parties taking turns.</summary>
        static void FakeVoice(LineRecordingPick pick, float t, out float level, out int speaker)
        {
            int seed = pick.ChosenCallId * 31 + pick.Index * 7;
            float turn = 1.3f + Mathf.PerlinNoise(seed * 0.37f, 0.5f) * 1.2f;
            int seg = Mathf.FloorToInt(t / turn);
            float inSeg = t - seg * turn;
            bool pause = inSeg > turn - 0.25f;
            speaker = pause ? LineIds.None : ((seg + seed) & 1) == 0 ? pick.Caller : pick.Callee;
            float syll = Mathf.Abs(Mathf.Sin(t * 9.5f + seed)) * 0.6f + Mathf.PerlinNoise(t * 6f, seed * 0.13f) * 0.55f;
            level = pause ? 0.05f : Mathf.Clamp01(0.2f + syll * 0.8f);
        }

        static float FakeShape(LineRecordingPick pick, int bar)
        {
            float t = bar / (float)Bars * (pick.Seconds > 0.1f ? pick.Seconds : 7f);
            float l; int s;
            FakeVoice(pick, t, out l, out s);
            return Mathf.Max(0.08f, l);
        }
    }
}
