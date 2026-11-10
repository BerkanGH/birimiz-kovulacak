using System;
using System.Collections.Generic;
using System.Text;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 minimal play UI (Phase 2a): one debug-style screen for every match phase. Header (phase, timer, my prim,
    /// voice mode), left column (players: suç board, call / whistle / investigate / transfer / text shortcuts), centre
    /// column (the phase: task choice and reveal, meeting dossier, leaks, ballots, crisis, final, results) and right
    /// column (phone and line board, transfers, events, private texts and meeting chat).
    /// <para>Sections are rebuilt only when the view, messages, events or a local selection changed, and a rebuilt
    /// section replaces the old one only when its rendered content differs, so buttons under the pointer survive
    /// unrelated view updates. Every request goes through <see cref="LineClient.Send"/>; the host validates it.</para>
    /// </summary>
    public sealed class PlayUi : UiKit.Screen
    {
        readonly Label _phase, _timer, _hint, _me, _voice, _toast;
        readonly Section _players, _content, _phone, _transfer, _events, _messages, _composeTargets;
        readonly VisualElement _tapeHost;
        readonly TapeDeck _tape;
        readonly TextField _msgField;
        readonly Label _composeInfo;
        readonly List<string> _feed = new List<string>();
        bool _dirty = true, _bound;
        float _toastUntil;
        int _transferTarget = LineIds.None, _transferAmount = 1;
        /// <summary>-2 = nobody chosen, -1 = meeting chat (everyone), else a player id.</summary>
        int _msgTarget = -2;
        int _week = int.MinValue;
        bool _ringing;

        public PlayUi(VisualElement parent) : base(parent, "pl-screen")
        {
            var sheet = Resources.Load<StyleSheet>("ui/styles/play");
            if (sheet != null) Root.styleSheets.Add(sheet);

            var header = Box("pl-header").AddTo(Root);
            _phase = Title("", "pl-phase").AddTo(header);
            _timer = Title("", "pl-timer").AddTo(header);
            _hint = Text("", "pl-hint").AddTo(header);
            _me = Bold("", "pl-me").AddTo(header);
            _voice = Text("", "pl-voice").AddTo(header);
            _toast = Bold("", "pl-toast").AddTo(header);

            var body = Box("pl-body").AddTo(Root);
            var left = Column(body, "pl-col-left");
            var center = Column(body, "pl-col-center");
            var right = Column(body, "pl-col-right");

            _players = new Section(left);
            _tapeHost = Box("pl-tape").AddTo(center);
            _tape = new TapeDeck(_tapeHost);
            _tapeHost.Show(false);
            _content = new Section(center);
            _phone = new Section(right);
            _transfer = new Section(right);
            _events = new Section(right);
            _messages = new Section(right);

            var compose = Box("pl-section", "pl-compose").AddTo(right);
            _composeInfo = Text("", "pl-text", "pl-dim").AddTo(compose);
            _composeTargets = new Section(compose);
            var row = Box("pl-row").AddTo(compose);
            _msgField = new TextField { maxLength = 200 }.Cls("pl-field", "grow").AddTo(row);
            _msgField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { SendMessage(); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            Btn(T("msg.send"), SendMessage, "pl-primary").AddTo(row);
        }

        static VisualElement Column(VisualElement body, string cls)
        {
            var sv = new ScrollView(ScrollViewMode.Vertical).Cls("pl-col", cls);
            sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            body.Add(sv);
            return sv.contentContainer;
        }

        /// <summary>The play screen always wants the cursor (no mouse-look while it is up).</summary>
        public bool WantsCursor => true;

        static string T(string key) { return PlayText.T(key); }
        static string F(string key, params object[] a) { return PlayText.F(key, a); }

        protected override void OnShow() { _dirty = true; }

        protected override void OnHide()
        {
            if (_ringing) { _ringing = false; Sfx.StopLoop("desk_ring"); }
        }

        void Bind()
        {
            var lc = LineClient.I;
            if (_bound || lc == null) return;
            _bound = true;
            lc.ViewChanged += OnView;
            lc.MessagesReceived += OnMessages;
            lc.EventReceived += OnEvent;
            lc.ResultReceived += OnResult;
        }

        /// <summary>UIRoot.Rebuild (language switch) drops this screen and builds a new one: stop listening then.</summary>
        bool Detached()
        {
            if (Root.panel != null) return false;
            var lc = LineClient.I;
            if (_bound && lc != null)
            {
                lc.ViewChanged -= OnView;
                lc.MessagesReceived -= OnMessages;
                lc.EventReceived -= OnEvent;
                lc.ResultReceived -= OnResult;
            }
            _bound = false;
            return true;
        }

        void OnView(LineView o, LineView n) { if (!Detached()) _dirty = true; }
        void OnMessages(IReadOnlyList<LineMessage> m) { if (!Detached()) _dirty = true; }

        void OnEvent(LineEvent e)
        {
            if (Detached()) return;
            var lc = LineClient.I;
            string line = PlayText.Event(e, lc != null ? lc.MySlot : -1);
            if (line == null) return;
            _feed.Add(line);
            if (_feed.Count > 10) _feed.RemoveAt(0);
            _dirty = true;
            if (e.Type == LineEventType.TransferReceived || e.Type == LineEventType.Whistled) Sfx.Play("notify");
        }

        void OnResult(LineActionResult r, LineAction req)
        {
            if (Detached() || r == null || r.Accepted) return;
            if (r.Failure == LineFailure.Duplicate) return;
            _toast.SetText(F("toast.rejected", req != null ? T("verb." + req.Verb) : "?", PlayText.Failure(r.Failure)));
            _toastUntil = Time.unscaledTime + 5f;
        }

        // ================================================================ per frame

        public override void Tick()
        {
            if (!Visible) return;
            Bind();
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            if (v == null) return;
            if (v.WeekId != _week)
            {
                _week = v.WeekId;
                _feed.Clear();
                _transferTarget = LineIds.None; _transferAmount = 1; _msgTarget = -2;
                _dirty = true;
            }
            double now = lc.Now;
            UpdateHeader(lc, v, now);
            HandleKeys(lc, v);
            UpdateRing(v);
            UpdateTape(v, now);
            if (_dirty)
            {
                _dirty = false;
                _players.Update(p => BuildPlayers(p, lc, v));
                _content.Update(p => BuildContent(p, lc, v, now));
                _phone.Update(p => BuildPhone(p, lc, v, now));
                _transfer.Update(p => BuildTransfer(p, lc, v));
                _events.Update(BuildEvents);
                _messages.Update(p => BuildMessages(p, lc, v));
                _composeTargets.Update(p => BuildComposeTargets(p, lc, v));
            }
        }

        void UpdateHeader(LineClient lc, LineView v, double now)
        {
            string title = PlayText.Phase(v.Phase);
            if (v.Task != null && (v.Phase == LinePhase.TaskPlay || v.Phase == LinePhase.TaskReveal || v.Phase == LinePhase.Break))
                title = F("header.task", v.Task.Number + 1, LineTasks.Count, PlayText.Task(v.Task.Kind)) + " · " + title;
            else if (v.Meeting != null && v.Meeting.Index < 2 && v.Phase >= LinePhase.MeetingDossier && v.Phase <= LinePhase.MeetingVerdict)
                title = F("header.meeting", v.Meeting.Index + 1) + " · " + title;
            if (v.Phase == LinePhase.FinalVote && v.Final != null)
                title += " · " + F(v.Final.Revote ? "header.final_revote" : "header.final_round", v.Final.Round + 1, Math.Max(1, v.Final.FiringsTotal));
            _phase.SetText(title);
            double left = Math.Max(0, v.PhaseEndTime - now);
            string timer = UiKit.Clock(left);
            if (v.Phase == LinePhase.FinalDefense && v.Final != null && v.Final.Defender >= 0)
                timer = UiKit.Clock(Math.Max(0, v.Final.DefenderEndTime - now)) + " / " + timer;
            if (v.Phase == LinePhase.Results || v.Phase == LinePhase.Abandoned) timer = v.Phase == LinePhase.Results ? timer : "";
            _timer.SetText(timer);
            _hint.SetText(PlayText.Hint(v.Phase));
            var m = v.Mine;
            var sb = new StringBuilder();
            sb.Append(lc.NameOf(lc.MySlot)).Append(" · ").Append(F("header.prim", m.Prim));
            if (v.Features.RecordingsEnabled) sb.Append(" · ").Append(F("header.recorder", m.RecorderUsesLeft));
            if (v.Day == 0) sb.Append(" · ").Append(T(m.InvestigationUsed ? "header.investigation_used" : "header.investigation_left"));
            _me.SetText(sb.ToString());
            string voice;
            if (v.Phase == LinePhase.FinalDefense && v.Final != null && v.Final.Defender >= 0) voice = F("voice.defense", PlayText.Name(v.Final.Defender));
            else if (v.Phase == LinePhase.Results || v.Phase == LinePhase.Abandoned) voice = T("voice.open");
            else voice = T(v.Features.OpenVoice ? "voice.open" : "voice.calls");
            var vs = VoiceSystem.Instance;
            if (vs != null) voice += " · " + F("voice.ptt", vs.PushToTalkKey);
            _voice.SetText(voice);
            if (_toastUntil > 0 && Time.unscaledTime > _toastUntil) { _toastUntil = 0; _toast.SetText(""); }
            _toast.Show(_toast.text.Length > 0);
        }

        void HandleKeys(LineClient lc, LineView v)
        {
            if (UIRoot.I != null && UIRoot.I.TextInputFocused) return;
            var ph = v.Mine.Phone;
            if (GameInput.AnswerCall != null && GameInput.AnswerCall.WasPressedThisFrame() && ph.Status == CallStatus.Ringing) lc.Send(LineVerb.Answer);
            if (GameInput.HangUp != null && GameInput.HangUp.WasPressedThisFrame())
            {
                if (ph.Status == CallStatus.Ringing) lc.Send(LineVerb.Decline);
                else if (ph.Status == CallStatus.Connected || ph.Status == CallStatus.Dialing) lc.Send(LineVerb.HangUp);
            }
        }

        void UpdateRing(LineView v)
        {
            bool ring = v.Mine.Phone.Status == CallStatus.Ringing;
            if (ring == _ringing) return;
            _ringing = ring;
            if (ring) Sfx.LoopAt("desk_ring", "ringtone", Vector3.zero, 0.55f, false);
            else Sfx.StopLoop("desk_ring");
        }

        /// <summary>The cassette deck shows the leak that is playing (or the next one within a few seconds).</summary>
        void UpdateTape(LineView v, double now)
        {
            LineRecordingPick show = null;
            if (v.Meeting != null)
                foreach (var p in v.Meeting.Recordings)
                    if (now >= p.PlayAt - 4 && now < p.PlayAt + p.Seconds + 3) { show = p; break; }
            _tapeHost.Show(show != null);
            if (show == null) return;
            _tape.Set(show, v);
            _tape.Tick(now, Time.unscaledDeltaTime);
        }

        // ================================================================ actions

        static void Send(LineVerb verb, int target = LineIds.None, int a = -1, string text = "")
        {
            var lc = LineClient.I;
            if (lc != null) lc.Send(verb, target, a, -1, -1, text);
        }

        void SendMessage()
        {
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            string text = (_msgField.value ?? "").Trim();
            if (v == null || text.Length == 0) return;
            int target = _msgTarget == -2 && v.Features.MeetingChatOpen ? -1 : _msgTarget;
            if (target == -1)
            {
                if (!v.Features.MeetingChatOpen) { _toast.SetText(T("msg.chat_closed")); _toastUntil = Time.unscaledTime + 4f; return; }
                Send(LineVerb.SendMeetingChat, text: text);
            }
            else if (target >= 0) Send(LineVerb.SendText, target, text: text);
            else { _toast.SetText(T("msg.pick_target")); _toastUntil = Time.unscaledTime + 4f; return; }
            _msgField.SetValueWithoutNotify("");
            _msgField.Focus();
        }

        // ================================================================ builders

        static Button Btn(string text, Action onClick, params string[] cls)
        {
            var b = new Button(() => { Sfx.Play("click"); onClick?.Invoke(); }) { text = text };
            b.enableRichText = false;
            b.AddToClassList("pl-btn");
            foreach (var c in cls) if (!string.IsNullOrEmpty(c)) b.AddToClassList(c);
            return b;
        }

        static Label Head(VisualElement p, string text) { return Title(text, "pl-title").AddTo(p); }
        static Label Sub(VisualElement p, string text) { return Bold(text, "pl-sub").AddTo(p); }
        static Label Line(VisualElement p, string text, params string[] cls) { return Text(text, "pl-text").Cls(cls).AddTo(p); }
        static VisualElement Row(VisualElement p) { return Box("pl-row").AddTo(p); }

        static Label Tag(VisualElement p, string text, string cls = null) { return Bold(text, "pl-tag").Cls(cls).AddTo(p); }

        // ---- players (left)

        void BuildPlayers(VisualElement p, LineClient lc, LineView v)
        {
            Head(p, T("players.title"));
            var f = v.Features;
            var m = v.Mine;
            int me = lc.MySlot;
            bool taskPhase = v.Phase == LinePhase.TaskPlay;
            bool votePhase = f.VoteOpen;
            foreach (var pl in v.Players)
            {
                var card = Box("pl-player").AddTo(p);
                var col = PlayerColor(pl.ColorIndex);
                card.style.borderLeftColor = col;
                var top = Row(card);
                Bold(pl.Name, "pl-pname").AddTo(top);
                if (pl.Id == me) Tag(top, T("players.me"), "pl-tag-me");
                if (pl.IsBot) Tag(top, T("players.bot"));
                if (!pl.Connected) Tag(top, T("players.offline"), "pl-tag-bad");
                if (pl.Candidate) Tag(top, T("players.candidate"), "pl-tag-warn");
                if (pl.Fired) Tag(top, T("players.fired"), "pl-tag-bad");
                if (InCall(v, pl.Id)) Tag(top, T("players.on_phone"));
                var info = Row(card);
                Bold(F("players.blame", pl.Blame), "pl-blame").AddTo(info);
                if (taskPhase) Line(info, T(pl.Submitted ? "players.submitted" : "players.choosing"), pl.Submitted ? "pl-good" : "pl-dim");
                if (votePhase) Line(info, T(pl.Voted ? "players.voted" : "players.voting"), pl.Voted ? "pl-good" : "pl-dim");
                if (pl.Id == me) continue;
                var acts = Box("pl-wrap").AddTo(card);
                int id = pl.Id;
                if (f.CallsOpen && m.Phone.Status == CallStatus.Idle && pl.Connected && !pl.IsBot) Btn(T("act.call"), () => Send(LineVerb.Call, id)).AddTo(acts);
                else if (f.CallsOpen && m.Phone.Status == CallStatus.Idle && pl.Connected) Btn(T("act.call"), () => Send(LineVerb.Call, id), "pl-dimbtn").AddTo(acts);
                if (f.WhistleOpen && m.WitnessOf.Contains(id)) Btn(T("act.whistle"), () => Send(LineVerb.Whistle, id), "pl-danger").AddTo(acts);
                if (f.InvestigationOpen && !m.InvestigationUsed && m.InvestigationTargets.Contains(id))
                    Btn(F("act.investigate", lc.Config.InvestigationCost), () => Send(LineVerb.Investigate, id), "pl-danger").AddTo(acts);
                if (f.TransfersOpen) Btn(T("act.transfer"), () => { _transferTarget = id; _dirty = true; }, _transferTarget == id ? "pl-on" : null).AddTo(acts);
                if (f.TextsOpen) Btn(T("act.text"), () => { _msgTarget = id; _dirty = true; _msgField.Focus(); }, _msgTarget == id ? "pl-on" : null).AddTo(acts);
            }
        }

        static bool InCall(LineView v, int id)
        {
            foreach (var c in v.LineBoard) if (c.Caller == id || c.Callee == id) return true;
            return false;
        }

        // ---- phase content (centre)

        void BuildContent(VisualElement p, LineClient lc, LineView v, double now)
        {
            switch (v.Phase)
            {
                case LinePhase.Intro: BuildIntro(p, lc, v); break;
                case LinePhase.TaskPlay:
                case LinePhase.TaskReveal:
                case LinePhase.Break:
                    BuildTask(p, lc, v); break;
                case LinePhase.Results: BuildResults(p, lc, v); return;
                case LinePhase.Abandoned:
                    Head(p, PlayText.Phase(v.Phase));
                    Line(p, T("abandoned.text"));
                    Btn(T("results.leave"), () => ConnectionManager.I?.Leave(), "pl-big").AddTo(p);
                    return;
            }
            // An open ballot comes first: it is the one thing to do in a vote phase.
            if (v.Features.VoteOpen) BuildVote(p, lc, v);
            if (v.Phase == LinePhase.CrisisCalls) BuildCrisisCalls(p, lc, v);
            if (v.Final != null) BuildFinal(p, lc, v);
            if (v.Meeting != null && (LineClient.IsMeetingPhase(v.Phase) || v.Phase == LinePhase.CrisisCalls)) BuildMeeting(p, lc, v, now);
        }

        void BuildIntro(VisualElement p, LineClient lc, LineView v)
        {
            Head(p, T("intro.title"));
            var c = lc.Config;
            Line(p, F("intro.goal", c.StartPrim));
            Line(p, T("intro.flow"));
            Line(p, F("intro.final", c.CandidatesFor(v.Players.Count), c.FiringsFor(v.Players.Count)));
            Line(p, T("intro.phone"));
            if (v.Features.RecordingsEnabled) Line(p, F("intro.recorder", c.RecorderUses));
            Line(p, F("intro.transfer", c.TransferMax));
        }

        void BuildTask(VisualElement p, LineClient lc, LineView v)
        {
            var t = v.Task;
            if (t == null) { Head(p, PlayText.Phase(v.Phase)); return; }
            int me = lc.MySlot;
            Head(p, F("task.title", t.Number + 1, PlayText.Task(t.Kind)));
            Line(p, PlayText.TaskRules(t, lc.Config, v.Players.Count));
            var others = t.Group.FindAll(id => id != me);
            if (t.Kind == TaskKind.WeekendShift) Line(p, F("task.everyone", t.Needed));
            else Line(p, F("task.group", PlayText.Names(others)));
            if (v.Phase == LinePhase.TaskPlay)
            {
                Sub(p, T("task.choose"));
                var row = Row(p);
                for (int choice = 0; choice <= 1; choice++)
                {
                    int c = choice;
                    bool on = t.Submitted && t.MyChoice == c;
                    Btn(PlayText.Choice(t.Kind, c), () => Send(LineVerb.SubmitTask, a: c), "pl-big", on ? "pl-on" : null).AddTo(row);
                }
                Line(p, t.Submitted ? F("task.submitted", PlayText.Choice(t.Kind, t.MyChoice)) : F("task.default", PlayText.Choice(t.Kind, 0)), t.Submitted ? "pl-good" : "pl-dim");
                return;
            }
            // reveal / break
            Sub(p, T("task.reveal"));
            if (t.Revealed)
            {
                foreach (var ch in t.Choices)
                    Line(p, PlayText.Name(ch.Player) + ": " + PlayText.Choice(t.Kind, ch.Choice) + (ch.Player == me && ch.Payout >= 0 ? " · " + F("task.payout", ch.Payout) : ""),
                        ch.Choice == LineChoice.Take && t.Kind != TaskKind.WeekendShift ? "pl-bad" : null);
                if (t.MyPayout >= 0) Line(p, F("task.my_payout", t.MyPayout), "pl-good");
                if (t.Takers >= 0) Line(p, F("task.public_takers", t.Takers));
                if (t.Kind == TaskKind.WeekendShift) Line(p, F("task.public_roster", PlayText.Names(t.Roster), t.Roster.Count, t.Needed));
            }
            else Line(p, T("task.not_revealed"), "pl-dim");
            if (v.Phase == LinePhase.Break)
            {
                Sub(p, T("break.title"));
                Line(p, T(t.Number == 0 ? "break.next_task" : "break.meeting")); // breaks follow tasks 1 and 2 only
                var m = v.Mine;
                if (v.Features.WhistleOpen && m.WitnessOf.Count > 0)
                {
                    foreach (int thief in m.WitnessOf)
                    {
                        int id = thief;
                        Line(p, F("break.witness", PlayText.Name(id), lc.Config.CashWhistleBlame, lc.Config.CashWhistleReward), "pl-bad");
                        Btn(F("break.whistle_btn", PlayText.Name(id)), () => Send(LineVerb.Whistle, id), "pl-danger", "pl-big").AddTo(p);
                    }
                }
                else if (v.Features.WhistleOpen) Line(p, T("break.no_witness"), "pl-dim");
            }
        }

        void BuildMeeting(VisualElement p, LineClient lc, LineView v, double now)
        {
            var mt = v.Meeting;
            var m = v.Mine;
            int me = lc.MySlot;
            if (mt.Index < 2)
            {
                Head(p, F("meeting.title", mt.Index + 1));
                Line(p, mt.FailedTasks.Count > 0 ? F("meeting.failed", Join(mt.FailedTasks)) : T("meeting.no_failures"), mt.FailedTasks.Count > 0 ? "pl-bad" : "pl-good");
                if (mt.VoteableCount >= 0) Line(p, mt.VoteableCount > 0 ? F("meeting.voteable", mt.VoteableCount) : T("meeting.no_vote"));
                if (mt.Roster.Count > 0) Line(p, F("meeting.roster", PlayText.Names(mt.Roster)));
            }
            if (mt.Dossier.Count > 0)
            {
                Sub(p, T(mt.Index < 2 ? "meeting.dossier" : "meeting.final_dossier"));
                foreach (var e in mt.Dossier) Line(p, "• " + PlayText.Dossier(e), e.Kind == DossierKind.Investigation || e.Kind == DossierKind.Leak ? "pl-warn" : null);
            }
            if (mt.BlameGained.Count > 0)
            {
                Sub(p, T("meeting.blame_gained"));
                foreach (var g in mt.BlameGained) Line(p, PlayText.Name(g.Player) + ": " + F("meeting.blame_plus", g.Amount));
            }
            // leaked recordings
            if (mt.Recordings.Count > 0)
            {
                Sub(p, T("leak.list"));
                foreach (var r in mt.Recordings)
                {
                    string state = r.Played ? T("leak.played") : r.Empty ? T("leak.empty") : now >= r.PlayAt ? T("leak.playing") : F("leak.queued", Math.Max(0, (int)Math.Ceiling(r.PlayAt - now)));
                    Line(p, F("leak.line", r.Index + 1, PlayText.Name(r.Leaker), PlayText.Name(r.Caller), PlayText.Name(r.Callee)) + " · " + state);
                }
            }
            // investigation (meeting 1)
            if (v.Features.InvestigationOpen)
            {
                Sub(p, T("investigate.title"));
                var c = lc.Config;
                if (m.InvestigationUsed) Line(p, T("investigate.used"), "pl-dim");
                else if (m.InvestigationTargets.Count == 0) Line(p, T("investigate.none"), "pl-dim");
                else
                {
                    Line(p, F("investigate.rules", c.InvestigationCost, c.InvestigationGuiltyBlame, c.InvestigationRefund, c.InvestigationFalseBlame));
                    var row = Row(p);
                    foreach (int t in m.InvestigationTargets)
                    {
                        int id = t;
                        Btn(F("investigate.btn", PlayText.Name(id)), () => Send(LineVerb.Investigate, id), "pl-danger").AddTo(row);
                    }
                    if (m.Prim < c.InvestigationCost) Line(p, F("investigate.poor", m.Prim), "pl-bad");
                }
            }
            // leak my recordings
            if (v.Features.LeakOpen && v.Features.RecordingsEnabled)
            {
                Sub(p, T("leak.mine"));
                if (m.Recorded.Count == 0) Line(p, T("leak.none"), "pl-dim");
                foreach (var r in m.Recorded)
                {
                    int callId = r.CallId;
                    var row = Row(p);
                    Line(row, F("leak.recorded", PlayText.Name(r.Other), UiKit.Clock(Math.Max(0, now - r.Start))));
                    if (r.Leaked) Line(row, T("leak.leaked"), "pl-dim");
                    else Btn(T("leak.btn"), () => Send(LineVerb.Leak, a: callId), "pl-danger").AddTo(row);
                }
            }
        }

        static string Join(List<TaskKind> tasks)
        {
            var sb = new StringBuilder();
            foreach (var t in tasks) { if (sb.Length > 0) sb.Append(", "); sb.Append(PlayText.Task(t)); }
            return sb.ToString();
        }

        void BuildCrisisCalls(VisualElement p, LineClient lc, LineView v)
        {
            Head(p, T("crisis.title"));
            Line(p, F("crisis.rules", lc.Config.CrisisBlame));
            Line(p, F("crisis.candidates_rule", lc.Config.CandidatesFor(v.Players.Count)));
        }

        void BuildFinal(VisualElement p, LineClient lc, LineView v)
        {
            var fv = v.Final;
            int me = lc.MySlot;
            if (v.Phase == LinePhase.CrisisVote) { Head(p, T("crisis.vote_title")); Line(p, F("crisis.rules", lc.Config.CrisisBlame)); }
            if (fv.Crisis.Count > 0)
            {
                Sub(p, T("crisis.result"));
                foreach (var g in fv.Crisis) Line(p, PlayText.Name(g.Player) + ": " + F("meeting.blame_plus", g.Amount));
            }
            if (fv.Candidates.Count > 0)
            {
                Sub(p, F("final.candidates", fv.FiringsTotal));
                foreach (int id in fv.DefenseOrder)
                {
                    var pl = lc.Player(id);
                    string line = PlayText.Name(id) + " · " + F("players.blame", pl != null ? pl.Blame : 0);
                    if (pl != null && pl.Fired) line += " · " + T("players.fired");
                    if (v.Phase == LinePhase.FinalDefense && fv.Defender == id) line += " · " + T("final.defending");
                    Line(p, line, fv.Defender == id && v.Phase == LinePhase.FinalDefense ? "pl-warn" : null);
                }
                var meP = lc.Player(me);
                if (meP != null && meP.Candidate && !meP.Fired) Line(p, T("final.you_candidate"), "pl-bad");
            }
            if (v.Phase == LinePhase.FinalDefense) Line(p, T("final.defense_rules"));
            if (fv.Firings.Count > 0)
            {
                Sub(p, T("final.firings"));
                foreach (var f in fv.Firings) Line(p, PlayText.Firing(f), "pl-bad");
            }
        }

        void BuildVote(VisualElement p, LineClient lc, LineView v)
        {
            var m = v.Mine;
            int me = lc.MySlot;
            var meP = lc.Player(me);
            Sub(p, T("vote.title." + v.Phase));
            if (meP != null && meP.Fired) { Line(p, T("vote.fired"), "pl-dim"); return; }
            var options = new List<int>();
            if (v.Phase == LinePhase.FinalVote) { if (v.Final != null) options.AddRange(v.Final.Options); }
            else foreach (var pl in v.Players) if (!pl.Fired) options.Add(pl.Id);
            options.Remove(me);
            for (int i = 0; i < m.Ballots.Count; i++)
            {
                int ballot = i;
                int a = v.Phase == LinePhase.MeetingVote ? i : 0;
                if (m.Ballots.Count > 1) Line(p, F("vote.ballot", i + 1, m.Ballots.Count));
                var row = Box("pl-wrap").AddTo(p);
                foreach (int id in options)
                {
                    int target = id;
                    Btn(PlayText.Name(target), () => Send(LineVerb.Vote, target, a), "pl-big", m.Ballots[ballot] == target ? "pl-on" : null).AddTo(row);
                }
                if (m.Ballots[ballot] >= 0) Btn(T(v.Phase == LinePhase.FinalVote ? "vote.abstain" : "vote.clear"), () => Send(LineVerb.Vote, LineIds.None, a)).AddTo(row);
            }
            int voted = 0, connected = 0;
            foreach (var pl in v.Players) { if (pl.Voted) voted++; if (pl.Connected && !pl.Fired) connected++; }
            Line(p, F("vote.progress", voted, connected), "pl-dim");
            Line(p, T("vote.note." + v.Phase), "pl-dim");
        }

        void BuildResults(VisualElement p, LineClient lc, LineView v)
        {
            var r = v.Results;
            Head(p, T("results.title"));
            var row = Row(p);
            if (HostLineController.Current != null) Btn(T("results.continue"), () => HostLineController.Current?.ContinueAfterResults(), "pl-primary", "pl-big").AddTo(row);
            Btn(T("results.again"), () => { LineClient.I?.RequestRematch(true); TelemetryHooks.RematchRequested(); _toast.SetText(T("results.again_sent")); _toastUntil = Time.unscaledTime + 5f; }, "pl-big").AddTo(row);
            Btn(T("results.leave"), () => ConnectionManager.I?.Leave(), "pl-big").AddTo(row);
            if (r == null) { Line(p, T("results.waiting"), "pl-dim"); return; }
            Line(p, F("results.winners", PlayText.Names(r.Winners)), "pl-good");
            Line(p, F("results.fired", PlayText.Names(r.Fired)), "pl-bad");
            Sub(p, T("results.table"));
            var players = new List<LineWeekPlayerResult>(r.Players);
            players.Sort((a, b) => b.Prim != a.Prim ? b.Prim.CompareTo(a.Prim) : a.Player.CompareTo(b.Player));
            foreach (var pr in players)
            {
                var sb = new StringBuilder();
                sb.Append(PlayText.Name(pr.Player)).Append(" · ").Append(F("header.prim", pr.Prim)).Append(" · ").Append(F("players.blame", pr.Blame));
                if (pr.Winner) sb.Append(" · ").Append(T("results.winner"));
                if (pr.Fired) sb.Append(" · ").Append(T("players.fired"));
                else if (pr.WasCandidate) sb.Append(" · ").Append(T("players.candidate"));
                if (pr.Recorded > 0 || pr.Leaked > 0) sb.Append(" · ").Append(F("results.recorded", pr.Recorded, pr.Leaked));
                Line(p, sb.ToString(), pr.Winner ? "pl-good" : pr.Fired ? "pl-bad" : null);
            }
            if (r.Firings.Count > 0)
            {
                Sub(p, T("final.firings"));
                foreach (var f in r.Firings) Line(p, PlayText.Firing(f));
            }
            Sub(p, T("results.choices"));
            foreach (var c in r.Choices) Line(p, F("results.choice", c.TaskNumber + 1, PlayText.Task(c.Task), PlayText.Name(c.Player), PlayText.Choice(c.Task, c.Choice), c.Payout));
            if (r.Incidents.Count > 0)
            {
                Sub(p, T("results.incidents"));
                foreach (var e in r.Incidents) Line(p, "• " + PlayText.Dossier(e));
            }
            Sub(p, T("results.ballots"));
            if (r.Ballots.Count == 0) Line(p, T("common.none"), "pl-dim");
            foreach (var b in r.Ballots) Line(p, PlayText.Ballot(b));
            Sub(p, T("results.transfers"));
            if (r.Transfers.Count == 0) Line(p, T("common.none"), "pl-dim");
            foreach (var t in r.Transfers) Line(p, F("results.transfer", PlayText.Name(t.From), PlayText.Name(t.To), t.Amount, PlayText.Phase(t.Phase)));
        }

        // ---- right column

        void BuildPhone(VisualElement p, LineClient lc, LineView v, double now)
        {
            var f = v.Features;
            var m = v.Mine;
            var ph = m.Phone;
            Head(p, T("phone.title"));
            switch (ph.Status)
            {
                case CallStatus.Idle:
                    Line(p, T(f.CallsOpen ? "phone.idle" : "phone.closed"), "pl-dim");
                    break;
                case CallStatus.Dialing:
                    Line(p, F("phone.dialing", PlayText.Name(ph.Other)));
                    Btn(T("phone.hangup") + " (" + KeyLabel(GameInput.HangUp) + ")", () => Send(LineVerb.HangUp), "pl-danger").AddTo(p);
                    break;
                case CallStatus.Ringing:
                {
                    Line(p, F("phone.ringing", PlayText.Name(ph.Other)), "pl-warn");
                    var row = Row(p);
                    Btn(T("phone.answer") + " (" + KeyLabel(GameInput.AnswerCall) + ")", () => Send(LineVerb.Answer), "pl-primary", "pl-big").AddTo(row);
                    Btn(T("phone.decline") + " (" + KeyLabel(GameInput.HangUp) + ")", () => Send(LineVerb.Decline), "pl-danger").AddTo(row);
                    break;
                }
                case CallStatus.Connected:
                {
                    Line(p, F("phone.connected", PlayText.Name(ph.Other)), "pl-good");
                    var row = Row(p);
                    Btn(T("phone.hangup") + " (" + KeyLabel(GameInput.HangUp) + ")", () => Send(LineVerb.HangUp), "pl-danger").AddTo(row);
                    bool recording = false;
                    foreach (var r in m.Recorded) if (r.CallId == ph.CallId) recording = true;
                    if (f.RecordingsEnabled)
                    {
                        if (recording) Line(p, T("phone.recording"), "pl-bad");
                        else if (m.RecorderUsesLeft > 0) Btn(F("phone.record", m.RecorderUsesLeft), () => Send(LineVerb.Record), "pl-danger").AddTo(row);
                        else Line(p, T("phone.recorder_empty"), "pl-dim");
                    }
                    break;
                }
            }
            if (v.LineBoard.Count > 0 || f.CallsOpen)
            {
                Sub(p, T("phone.board"));
                if (v.LineBoard.Count == 0) Line(p, T("phone.board_empty"), "pl-dim");
                foreach (var c in v.LineBoard)
                    Line(p, PlayText.Name(c.Caller) + " → " + PlayText.Name(c.Callee) + " · " + T(c.Status == CallStatus.Connected ? "phone.board_talking" : "phone.board_ringing"));
            }
            if (m.CallLog.Count > 0)
            {
                Sub(p, T("phone.log"));
                for (int i = m.CallLog.Count - 1, n = 0; i >= 0 && n < 6; i--, n++)
                {
                    var c = m.CallLog[i];
                    string dir = T(c.Outgoing ? "phone.log_out" : "phone.log_in");
                    string len = c.Connected ? (c.End >= 0 ? UiKit.Clock(c.End - c.Start) : T("phone.log_active")) : T("phone.log_missed." + c.EndReason);
                    Line(p, dir + " " + PlayText.Name(c.Other) + " · " + len, "pl-dim");
                }
            }
        }

        static string KeyLabel(UnityEngine.InputSystem.InputAction a) { return a != null ? GameInput.KeyLabel(a) : "?"; }

        void BuildTransfer(VisualElement p, LineClient lc, LineView v)
        {
            var f = v.Features;
            var m = v.Mine;
            int me = lc.MySlot;
            if (!f.TransfersOpen && m.Transfers.Count == 0) return;
            Head(p, T("transfer.title"));
            var meP = lc.Player(me);
            if (f.TransfersOpen)
            {
                if (meP != null && meP.Candidate) Line(p, T("transfer.sealed"), "pl-bad");
                Line(p, F("transfer.rules", lc.Config.TransferMax), "pl-dim");
                var targets = Box("pl-wrap").AddTo(p);
                foreach (var pl in v.Players)
                {
                    if (pl.Id == me) continue;
                    int id = pl.Id;
                    Btn(pl.Name, () => { _transferTarget = id; _dirty = true; }, _transferTarget == id ? "pl-on" : null).AddTo(targets);
                }
                int max = Math.Max(1, Math.Min(lc.Config.TransferMax, m.Prim));
                _transferAmount = Mathf.Clamp(_transferAmount, 1, max);
                var row = Row(p);
                Btn("−", () => { _transferAmount = Math.Max(1, _transferAmount - 1); _dirty = true; }).AddTo(row);
                Bold(F("transfer.amount", _transferAmount), "pl-amount").AddTo(row);
                Btn("+", () => { _transferAmount = Math.Min(Math.Max(1, Math.Min(lc.Config.TransferMax, LineClient.I.View.Mine.Prim)), _transferAmount + 1); _dirty = true; }).AddTo(row);
                var send = Btn(_transferTarget >= 0 ? F("transfer.send", _transferAmount, PlayText.Name(_transferTarget)) : T("transfer.pick"), () =>
                {
                    if (_transferTarget >= 0) Send(LineVerb.Transfer, _transferTarget, _transferAmount);
                }, "pl-primary").AddTo(p);
                send.SetEnabled(_transferTarget >= 0 && m.Prim > 0);
            }
            if (m.Transfers.Count > 0)
            {
                Sub(p, T("transfer.log"));
                foreach (var t in m.Transfers)
                    Line(p, t.From == me ? F("transfer.out", PlayText.Name(t.To), t.Amount) : F("transfer.in", PlayText.Name(t.From), t.Amount), t.From == me ? "pl-bad" : "pl-good");
            }
        }

        void BuildEvents(VisualElement p)
        {
            if (_feed.Count == 0) return;
            Head(p, T("events.title"));
            for (int i = _feed.Count - 1; i >= 0; i--) Line(p, _feed[i], "pl-dim");
        }

        void BuildMessages(VisualElement p, LineClient lc, LineView v)
        {
            Head(p, T("msg.title"));
            var msgs = lc.Messages;
            if (msgs.Count == 0) Line(p, T("msg.empty"), "pl-dim");
            int me = lc.MySlot;
            for (int i = Math.Max(0, msgs.Count - 30); i < msgs.Count; i++)
            {
                var msg = msgs[i];
                string who;
                if (msg.Kind == MessageKind.MeetingChat) who = F("msg.chat_from", PlayText.Name(msg.From));
                else if (msg.Kind == MessageKind.System) who = T("msg.system");
                else if (msg.From == me) who = F("msg.text_to", PlayText.Name(msg.To));
                else who = F("msg.text_from", PlayText.Name(msg.From));
                Line(p, who + " " + msg.Text, msg.Kind == MessageKind.MeetingChat ? null : "pl-private");
            }
        }

        void BuildComposeTargets(VisualElement p, LineClient lc, LineView v)
        {
            var f = v.Features;
            var m = v.Mine;
            int me = lc.MySlot;
            int target = _msgTarget == -2 && f.MeetingChatOpen ? -1 : _msgTarget;
            _composeInfo.SetText(target == -1 ? F("msg.to_all", m.MeetingChatLeft) : target >= 0 ? F("msg.to", PlayText.Name(target), m.TextsLeft) : T("msg.pick_target"));
            var row = Box("pl-wrap").AddTo(p);
            if (f.MeetingChatOpen) Btn(T("msg.all"), () => { _msgTarget = -1; _dirty = true; }, target == -1 ? "pl-on" : null).AddTo(row);
            if (f.TextsOpen)
                foreach (var pl in v.Players)
                {
                    if (pl.Id == me) continue;
                    int id = pl.Id;
                    Btn(pl.Name, () => { _msgTarget = id; _dirty = true; }, target == id ? "pl-on" : null).AddTo(row);
                }
        }

        // ================================================================ section with content diff

        /// <summary>
        /// A rebuildable block. <see cref="Update"/> builds a fresh subtree and swaps it in only when its rendered
        /// content (element types, classes, texts, enabled state) differs from what is shown.
        /// </summary>
        sealed class Section
        {
            readonly VisualElement _holder;
            VisualElement _current;
            string _dump = "";

            public Section(VisualElement parent) { _holder = Box("pl-section").AddTo(parent); }

            public void Update(Action<VisualElement> build)
            {
                var next = Box("pl-section-body");
                try { build(next); }
                catch (Exception e) { Debug.LogException(e); next.Clear(); Text("UI: " + e.GetType().Name, "pl-text", "pl-bad").AddTo(next); }
                var sb = new StringBuilder(256);
                Dump(next, sb);
                string dump = sb.ToString();
                if (_current != null && dump == _dump) return;
                _dump = dump;
                _current?.RemoveFromHierarchy();
                _current = next;
                _holder.Add(next);
                _holder.Show(next.childCount > 0);
            }

            static void Dump(VisualElement e, StringBuilder sb)
            {
                sb.Append('<').Append(e.GetType().Name);
                foreach (var c in e.GetClasses()) sb.Append('.').Append(c);
                if (e is TextElement te) sb.Append('|').Append(te.text);
                if (!e.enabledSelf) sb.Append("|off");
                sb.Append(':');
                foreach (var child in e.Children()) Dump(child, sb);
                sb.Append('>');
            }
        }
    }
}
