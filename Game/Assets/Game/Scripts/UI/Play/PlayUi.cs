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
    /// v5 play UI: one screen for every match phase. Header (phase, timer, my prim, voice mode), left column (players:
    /// suç board, call / whistle / investigate / transfer / text shortcuts), centre column (a "what do you do now?"
    /// focus card with this phase's main actions — task choice, whistle, ballots, defence order, the desk reveal, the
    /// results summary — then the cassette deck while a leak plays, then secondary information: task rules, dossier,
    /// candidates, results details) and right column (phone and line board, transfers, events, private texts and
    /// meeting chat).
    /// <para>Sections are rebuilt only when the view, messages, events or a local selection changed, and a rebuilt
    /// section replaces the old one only when its rendered content differs, so buttons under the pointer survive
    /// unrelated view updates. Time-driven texts (call duration, ringing, leak and defence countdowns) and fast-changing
    /// status marks ("voted", "on the phone") are live labels: their text is refreshed at 2 Hz in place and left out of
    /// the content diff. Every request goes through <see cref="LineClient.Send"/>; the host validates it.</para>
    /// </summary>
    public sealed class PlayUi : UiKit.Screen
    {
        readonly Label _phase, _timer, _prim, _me, _voice, _toast;
        readonly Section _players, _focus, _content, _phone, _transfer, _events, _messages, _composeTargets;
        readonly Section[] _sections;
        readonly VisualElement _tapeHost;
        readonly TapeDeck _tape;
        readonly TextField _msgField;
        readonly Label _composeInfo;
        readonly List<string> _feed = new List<string>();
        bool _dirty = true, _bound;
        float _toastUntil, _nextLive;
        int _transferTarget = LineIds.None, _transferAmount = 1;
        /// <summary>-2 = nobody chosen, -1 = meeting chat (everyone), else a player id.</summary>
        int _msgTarget = -2;
        int _week = int.MinValue;
        bool _ringing;
        string _resultsTab = TabBallots;

        const string TabFirings = "firings", TabBallots = "ballots", TabTransfers = "transfers", TabChoices = "choices", TabIncidents = "incidents";
        /// <summary>UI mocks (Dev/LineMock.cs): switch the results details to this tab ("ballots", "firings", "choices", "transfers", "incidents").</summary>
        public static string MockResultsTab;
        const int Info = 0, Todo = 1, Done = 2;

        public PlayUi(VisualElement parent) : base(parent, "pl-screen")
        {
            var sheet = Resources.Load<StyleSheet>("ui/styles/play");
            if (sheet != null) Root.styleSheets.Add(sheet);

            var header = Box("pl-header").AddTo(Root);
            _phase = Title("", "pl-phase").AddTo(header);
            _timer = Title("", "pl-timer").AddTo(header);
            Box("pl-spacer").AddTo(header);
            _prim = Title("", "pl-prim").AddTo(header);
            _me = Bold("", "pl-me").AddTo(header);
            _voice = Text("", "pl-voice").AddTo(header);
            _toast = Bold("", "pl-toast").AddTo(header);

            var body = Box("pl-body").AddTo(Root);
            var left = Column(body, "pl-col-left");
            var center = Column(body, "pl-col-center");
            var right = Column(body, "pl-col-right");

            _players = new Section(left);
            _focus = new Section(center);
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
            _sections = new[] { _players, _focus, _content, _phone, _transfer, _events, _messages, _composeTargets };
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
        static string Name(int id) { return PlayText.Name(id); }

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
                _transferTarget = LineIds.None; _transferAmount = 1; _msgTarget = -2; _resultsTab = TabBallots;
                _dirty = true;
            }
            if (MockResultsTab != null) { _resultsTab = MockResultsTab; MockResultsTab = null; _dirty = true; }
            double now = lc.Now;
            UpdateHeader(lc, v, now);
            HandleKeys(lc, v);
            UpdateRing(v);
            UpdateTape(v, now);
            if (_dirty)
            {
                _dirty = false;
                _players.Update(p => BuildPlayers(p, lc, v, now));
                _focus.Update(p => BuildFocus(p, lc, v, now));
                _content.Update(p => BuildContent(p, lc, v, now));
                _phone.Update(p => BuildPhone(p, lc, v, now));
                _transfer.Update(p => BuildTransfer(p, lc, v));
                _events.Update(BuildEvents);
                _messages.Update(p => BuildMessages(p, lc, v));
                _composeTargets.Update(p => BuildComposeTargets(p, lc, v));
            }
            // live labels: countdowns and status marks, refreshed in place (≤ 2 Hz), never a section rebuild
            if (Time.unscaledTime >= _nextLive)
            {
                _nextLive = Time.unscaledTime + 0.5f;
                foreach (var s in _sections) s.TickLive(now);
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
            _timer.SetText(v.Phase == LinePhase.Abandoned ? "" : UiKit.Clock(left));
            var m = v.Mine;
            _prim.SetText(F("header.my_prim", m.Prim));
            var sb = new StringBuilder();
            sb.Append(lc.NameOf(lc.MySlot));
            if (v.Features.RecordingsEnabled) sb.Append(" · ").Append(F("header.recorder", m.RecorderUsesLeft));
            if (v.Day == 0) sb.Append(" · ").Append(T(m.InvestigationUsed ? "header.investigation_used" : "header.investigation_left"));
            _me.SetText(sb.ToString());
            string voice;
            if (v.Phase == LinePhase.FinalDefense && v.Final != null && v.Final.Defender >= 0) voice = F("voice.defense", Name(v.Final.Defender));
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

        /// <summary>I am a candidate whose desk is sealed (no outgoing transfers), or fired (nothing left to send).</summary>
        static bool CannotSend(LineClient lc)
        {
            var me = lc.Player(lc.MySlot);
            return me != null && (me.Candidate || me.Fired);
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
        static Label Big(VisualElement p, string text, params string[] cls) { return Bold(text, "pl-focus-text").Cls(cls).AddTo(p); }
        static VisualElement Row(VisualElement p) { return Box("pl-row").AddTo(p); }
        static VisualElement Wrap(VisualElement p) { return Box("pl-wrap").AddTo(p); }

        static Label Tag(VisualElement p, string text, string cls = null) { return Bold(text, "pl-tag").Cls(cls).AddTo(p); }

        /// <summary>
        /// A label whose text (and classes) <paramref name="apply"/> refreshes at 2 Hz without rebuilding its section.
        /// Its text is not part of the section's content diff: put everything the closure depends on into
        /// <paramref name="key"/>, or let the closure read the current view (<see cref="LineClient.View"/>).
        /// </summary>
        static Label Live(VisualElement p, string key, double now, Action<Label, double> apply, params string[] cls)
        {
            var l = Text("", "pl-text").Cls(cls).AddTo(p);
            l.userData = new LiveLabel { Key = key, Apply = apply };
            apply(l, now);
            return l;
        }

        static Label Live(VisualElement p, string key, double now, Func<double, string> text, params string[] cls)
        {
            return Live(p, key, now, (l, t) => l.SetText(text(t)), cls);
        }

        // ---- players (left)

        void BuildPlayers(VisualElement p, LineClient lc, LineView v, double now)
        {
            Head(p, T("players.title"));
            var f = v.Features;
            var m = v.Mine;
            int me = lc.MySlot;
            bool taskPhase = v.Phase == LinePhase.TaskPlay;
            bool votePhase = f.VoteOpen;
            bool canSend = f.TransfersOpen && !CannotSend(lc);
            foreach (var pl in v.Players)
            {
                var card = Box("pl-player").AddTo(p);
                var col = PlayerColor(pl.ColorIndex);
                card.style.borderLeftColor = col;
                var top = Row(card);
                Bold(pl.Name, "pl-pname").AddTo(top);
                int id = pl.Id;
                if (id == me) Tag(top, T("players.me"), "pl-tag-me");
                if (pl.IsBot) Tag(top, T("players.bot"));
                if (!pl.Connected) Tag(top, T("players.offline"), "pl-tag-bad");
                if (pl.Candidate) Tag(top, T("players.candidate"), "pl-tag-warn");
                if (pl.Candidate && !pl.Fired && v.Phase < LinePhase.Results) Tag(top, T("players.sealed"));
                if (pl.Fired) Tag(top, T("players.fired"), "pl-tag-bad");
                if (f.CallsOpen)
                {
                    var phone = Live(top, "phone" + id, now, (l, t) =>
                    {
                        var cur = LineClient.I != null ? LineClient.I.View : null;
                        l.Show(cur != null && InCall(cur, id));
                    }, "pl-tag");
                    phone.text = T("players.on_phone");
                }
                var info = Row(card);
                Bold(F("players.blame", pl.Blame), "pl-blame").AddTo(info);
                if (taskPhase || votePhase)
                {
                    bool task = taskPhase;
                    Live(info, (task ? "sub" : "vote") + id, now, (l, t) =>
                    {
                        var cur = LineClient.I != null ? LineClient.I.Player(id) : null;
                        bool yes = cur != null && (task ? cur.Submitted : cur.Voted);
                        l.SetText(T(task ? (yes ? "players.submitted" : "players.choosing") : (yes ? "players.voted" : "players.voting")));
                        l.EnableInClassList("pl-good", yes);
                        l.EnableInClassList("pl-dim", !yes);
                    });
                }
                if (id == me) continue;
                var acts = Wrap(card);
                if (f.CallsOpen && m.Phone.Status == CallStatus.Idle && pl.Connected && !pl.IsBot) Btn(T("act.call"), () => Send(LineVerb.Call, id)).AddTo(acts);
                else if (f.CallsOpen && m.Phone.Status == CallStatus.Idle && pl.Connected) Btn(T("act.call"), () => Send(LineVerb.Call, id), "pl-dimbtn").AddTo(acts);
                if (f.WhistleOpen && m.WitnessOf.Contains(id)) Btn(T("act.whistle"), () => Send(LineVerb.Whistle, id), "pl-danger").AddTo(acts);
                if (f.InvestigationOpen && !m.InvestigationUsed && m.InvestigationTargets.Contains(id))
                    Btn(F("act.investigate", lc.Config.InvestigationCost), () => Send(LineVerb.Investigate, id), "pl-danger").AddTo(acts);
                if (canSend) Btn(T("act.transfer"), () => { _transferTarget = id; _dirty = true; }, _transferTarget == id ? "pl-on" : null).AddTo(acts);
                if (f.TextsOpen) Btn(T("act.text"), () => { _msgTarget = id; _dirty = true; _msgField.Focus(); }, _msgTarget == id ? "pl-on" : null).AddTo(acts);
            }
        }

        static bool InCall(LineView v, int id)
        {
            foreach (var c in v.LineBoard) if (c.Caller == id || c.Callee == id) return true;
            return false;
        }

        // ---- focus card (top of the centre column): what to do now, with this phase's main actions

        void BuildFocus(VisualElement p, LineClient lc, LineView v, double now)
        {
            if (v.Phase == LinePhase.None) return;
            var card = Box("pl-focus").AddTo(p);
            var head = Row(card);
            string kicker = v.Phase == LinePhase.Results ? "focus.kicker_results" : v.Phase == LinePhase.FinalVerdict ? "focus.kicker_verdict" : "focus.kicker";
            Bold(T(kicker), "pl-focus-kicker", "grow").AddTo(head);
            int page = HowToPage(v);
            if (page >= 0) Btn(T("focus.rules"), () => HowToPlayScreen.OpenAt(page), "pl-small").AddTo(head);
            int state = Info;
            switch (v.Phase)
            {
                case LinePhase.Intro:
                    Big(card, T("hint.Intro"));
                    Line(card, F("focus.intro_goal", lc.Config.StartPrim));
                    break;
                case LinePhase.TaskPlay: state = FocusTask(card, lc, v); break;
                case LinePhase.TaskReveal:
                case LinePhase.Break: state = FocusReveal(card, lc, v); break;
                case LinePhase.MeetingDossier:
                case LinePhase.MeetingDiscussion: FocusMeeting(card, lc, v); break;
                case LinePhase.MeetingVote:
                case LinePhase.CrisisVote:
                case LinePhase.FinalVote: state = FocusVote(card, lc, v, now); break;
                case LinePhase.MeetingVerdict: FocusMeetingVerdict(card, v); break;
                case LinePhase.CrisisCalls:
                    Big(card, F("focus.crisis_calls", lc.Config.CrisisBlame));
                    Line(card, F("crisis.candidates_rule", lc.Config.CandidatesFor(v.Players.Count)));
                    Line(card, T("focus.crisis_how"), "pl-dim");
                    break;
                case LinePhase.CrisisVerdict: FocusCrisisVerdict(card, lc, v); break;
                case LinePhase.FinalDiscussion: FocusFinalDiscussion(card, lc, v); break;
                case LinePhase.FinalDefense: state = FocusDefense(card, lc, v, now); break;
                case LinePhase.FinalVerdict: FocusVerdict(card, lc, v); break;
                case LinePhase.Results: FocusResults(card, lc, v); break;
                case LinePhase.Abandoned:
                    Big(card, T("abandoned.text"));
                    Btn(T("results.leave"), () => ConnectionManager.I?.Leave(), "pl-big").AddTo(card);
                    break;
            }
            card.EnableInClassList("pl-focus-todo", state == Todo);
            card.EnableInClassList("pl-focus-done", state == Done);
        }

        /// <summary>The how-to page that explains this phase (-1 = none).</summary>
        static int HowToPage(LineView v)
        {
            switch (v.Phase)
            {
                case LinePhase.Intro: return HowToPlayScreen.PageGoal;
                case LinePhase.TaskPlay:
                case LinePhase.TaskReveal:
                case LinePhase.Break:
                    if (v.Task == null) return HowToPlayScreen.PageFlow;
                    return v.Task.Kind == TaskKind.ClientBonus ? HowToPlayScreen.PageBonus : v.Task.Kind == TaskKind.PettyCash ? HowToPlayScreen.PageCash : HowToPlayScreen.PageShift;
                case LinePhase.MeetingDossier:
                case LinePhase.MeetingDiscussion: return v.Day == 0 ? HowToPlayScreen.PageInvestigation : HowToPlayScreen.PageBlame;
                case LinePhase.MeetingVote:
                case LinePhase.MeetingVerdict: return HowToPlayScreen.PageBlame;
                case LinePhase.CrisisCalls:
                case LinePhase.CrisisVote:
                case LinePhase.CrisisVerdict: return HowToPlayScreen.PageCrisis;
                case LinePhase.FinalDiscussion:
                case LinePhase.FinalDefense:
                case LinePhase.FinalVote:
                case LinePhase.FinalVerdict: return HowToPlayScreen.PageFinal;
                default: return -1;
            }
        }

        int FocusTask(VisualElement card, LineClient lc, LineView v)
        {
            var t = v.Task;
            if (t == null) { Big(card, T("hint.TaskPlay")); return Info; }
            int me = lc.MySlot;
            var c = lc.Config;
            switch (t.Kind)
            {
                case TaskKind.ClientBonus: Big(card, T("focus.task.client_bonus")); break;
                case TaskKind.PettyCash: Big(card, T("focus.task.petty_cash")); break;
                case TaskKind.WeekendShift: Big(card, F("focus.task.weekend_shift", c.ShiftSkipPay, t.Needed >= 0 ? t.Needed : LineConfig.ShiftNeeded(v.Players.Count))); break;
                default: Big(card, T("hint.TaskPlay")); break;
            }
            if (t.Kind == TaskKind.WeekendShift) Line(card, F("task.everyone", t.Needed >= 0 ? t.Needed : LineConfig.ShiftNeeded(v.Players.Count)));
            else Line(card, F("task.group", PlayText.Names(t.Group.FindAll(id => id != me))));
            var row = Wrap(card);
            for (int choice = 0; choice <= 1; choice++)
            {
                int ch = choice;
                bool on = t.Submitted && t.MyChoice == ch;
                Btn(PlayText.Choice(t.Kind, ch), () => Send(LineVerb.SubmitTask, a: ch), "pl-choice", on ? "pl-on" : null).AddTo(row);
            }
            Line(card, t.Submitted ? F("task.submitted", PlayText.Choice(t.Kind, t.MyChoice)) : F("task.default", PlayText.Choice(t.Kind, 0)), t.Submitted ? "pl-good" : "pl-warn");
            return t.Submitted ? Done : Todo;
        }

        int FocusReveal(VisualElement card, LineClient lc, LineView v)
        {
            var t = v.Task;
            if (t == null) { Big(card, PlayText.Hint(v.Phase)); return Info; }
            int me = lc.MySlot;
            var m = v.Mine;
            var c = lc.Config;
            int state = Info;
            bool witness = v.Phase == LinePhase.Break && v.Features.WhistleOpen && m.WitnessOf.Count > 0;
            if (witness)
            {
                foreach (int thief in m.WitnessOf)
                {
                    int id = thief;
                    Big(card, F("break.witness", Name(id), c.CashWhistleBlame, c.CashWhistleReward), "pl-bad");
                    Btn(F("break.whistle_btn", Name(id)), () => Send(LineVerb.Whistle, id), "pl-danger", "pl-choice").AddTo(card);
                }
                Line(card, T("focus.witness_or"), "pl-dim");
                state = Todo;
            }
            else if (v.Phase == LinePhase.Break && v.Features.WhistleOpen && SoleTaker(t, me)) Big(card, T("focus.thief"), "pl-warn");
            else if (v.Phase == LinePhase.Break) Big(card, T(t.Number == 0 ? "break.next_task" : "break.meeting")); // breaks follow tasks 1 and 2 only
            else Big(card, T("hint.TaskReveal"));
            if (!t.Revealed) { Line(card, T("task.not_revealed"), "pl-dim"); return state; }
            var row = Wrap(card);
            foreach (var ch in t.Choices)
            {
                bool bad = ch.Choice == LineChoice.Take && t.Kind != TaskKind.WeekendShift;
                Bold(Name(ch.Player) + ": " + PlayText.Choice(t.Kind, ch.Choice), "pl-chip", bad ? "pl-chip-bad" : null).AddTo(row);
            }
            if (t.MyPayout >= 0) Bold(F("task.my_payout", t.MyPayout), "pl-good", "pl-sub").AddTo(card);
            return state;
        }

        /// <summary>Petty Cash: I am the only taker in my group (a witness may whistle on me in the break).</summary>
        static bool SoleTaker(LineTaskView t, int me)
        {
            if (t.Kind != TaskKind.PettyCash || !t.Revealed) return false;
            int takers = 0;
            bool mine = false;
            foreach (var ch in t.Choices)
                if (ch.Choice == LineChoice.Take) { takers++; if (ch.Player == me) mine = true; }
            return mine && takers == 1;
        }

        void FocusMeeting(VisualElement card, LineClient lc, LineView v)
        {
            var mt = v.Meeting;
            var m = v.Mine;
            var c = lc.Config;
            if (mt == null) { Big(card, PlayText.Hint(v.Phase)); return; }
            if (mt.FailedTasks.Count == 0) Big(card, T("focus.meeting_clean"));
            else
            {
                Big(card, T(mt.Index == 0 ? "focus.meeting1" : "focus.meeting2"));
                Line(card, F("meeting.failed", Join(mt.FailedTasks)), "pl-bad");
                Line(card, T("focus.meeting_vote_next"));
            }
            bool investigate = v.Features.InvestigationOpen && !m.InvestigationUsed && m.InvestigationTargets.Count > 0;
            bool leak = LeakableCount(v) > 0;
            if (!investigate && !leak) return;
            Sub(card, T("focus.optional"));
            var row = Wrap(card);
            if (investigate)
                foreach (int t in m.InvestigationTargets)
                {
                    int id = t;
                    Btn(F("investigate.btn", Name(id), c.InvestigationCost), () => Send(LineVerb.Investigate, id), "pl-danger", "pl-big").AddTo(row);
                }
            LeakButtons(row, v);
            if (investigate) Line(card, F("investigate.rules", c.InvestigationCost, c.InvestigationGuiltyBlame, c.InvestigationRefund, c.InvestigationFalseBlame), "pl-dim");
        }

        static int LeakableCount(LineView v)
        {
            if (!v.Features.LeakOpen || !v.Features.RecordingsEnabled) return 0;
            int n = 0;
            foreach (var r in v.Mine.Recorded) if (!r.Leaked) n++;
            return n;
        }

        static void LeakButtons(VisualElement row, LineView v)
        {
            if (LeakableCount(v) == 0) return;
            foreach (var r in v.Mine.Recorded)
            {
                if (r.Leaked) continue;
                int callId = r.CallId;
                Btn(F("focus.leak_btn", Name(r.Other)), () => Send(LineVerb.Leak, a: callId), "pl-danger", "pl-big").AddTo(row);
            }
        }

        int FocusVote(VisualElement card, LineClient lc, LineView v, double now)
        {
            var m = v.Mine;
            int me = lc.MySlot;
            var meP = lc.Player(me);
            var c = lc.Config;
            if (meP != null && meP.Fired) { Big(card, T("focus.fired"), "pl-dim"); return Info; }
            string text = v.Phase == LinePhase.MeetingVote ? F("focus.vote.MeetingVote", m.Ballots.Count, c.BlamePerVote)
                : v.Phase == LinePhase.CrisisVote ? F("focus.vote.CrisisVote", c.CrisisBlame)
                : v.Final != null && v.Final.Revote ? T("focus.vote.revote") : T("focus.vote.FinalVote");
            Big(card, text);
            var options = new List<int>();
            if (v.Phase == LinePhase.FinalVote) { if (v.Final != null) options.AddRange(v.Final.Options); }
            else foreach (var pl in v.Players) if (!pl.Fired) options.Add(pl.Id);
            options.Remove(me);
            bool empty = m.Ballots.Count == 0;
            for (int i = 0; i < m.Ballots.Count; i++)
            {
                int ballot = i;
                int a = v.Phase == LinePhase.MeetingVote ? i : 0;
                if (m.Ballots.Count > 1) Sub(card, F("vote.ballot", i + 1, m.Ballots.Count));
                var row = Wrap(card);
                foreach (int id in options)
                {
                    int target = id;
                    Btn(Name(target), () => Send(LineVerb.Vote, target, a), "pl-choice", m.Ballots[ballot] == target ? "pl-on" : null).AddTo(row);
                }
                if (m.Ballots[ballot] >= 0) Btn(T(v.Phase == LinePhase.FinalVote ? "vote.abstain" : "vote.clear"), () => Send(LineVerb.Vote, LineIds.None, a), "pl-small").AddTo(row);
                else empty = true;
            }
            Line(card, empty ? T("vote.todo." + v.Phase) : T("vote.done"), empty ? "pl-warn" : "pl-good");
            // read the current view: other players voting must not rebuild the ballot buttons
            Live(card, "progress", now, t =>
            {
                var cur = LineClient.I != null ? LineClient.I.View : null;
                int voted = 0, connected = 0;
                if (cur != null) foreach (var pl in cur.Players) { if (pl.Voted) voted++; if (pl.Connected && !pl.Fired) connected++; }
                return F("vote.progress", voted, connected);
            }, "pl-dim");
            return empty ? Todo : Done;
        }

        void FocusMeetingVerdict(VisualElement card, LineView v)
        {
            Big(card, T("hint.MeetingVerdict"));
            var mt = v.Meeting;
            if (mt == null || mt.BlameGained.Count == 0) { Line(card, T("focus.no_blame"), "pl-dim"); return; }
            var row = Wrap(card);
            foreach (var g in mt.BlameGained) Bold(Name(g.Player) + " " + F("meeting.blame_plus", g.Amount), "pl-chip", "pl-chip-warn").AddTo(row);
        }

        void FocusCrisisVerdict(VisualElement card, LineClient lc, LineView v)
        {
            Big(card, T("hint.CrisisVerdict"));
            var fv = v.Final;
            if (fv == null) return;
            if (fv.Crisis.Count > 0)
            {
                var row = Wrap(card);
                foreach (var g in fv.Crisis) Bold(Name(g.Player) + " " + F("meeting.blame_plus", g.Amount), "pl-chip", "pl-chip-warn").AddTo(row);
            }
            else Line(card, T("crisis.none"), "pl-dim");
            if (fv.Candidates.Count > 0) Line(card, F("focus.candidates", PlayText.Names(fv.DefenseOrder), fv.FiringsTotal), "pl-warn");
            Line(card, T("focus.sealed_note"));
            var meP = lc.Player(lc.MySlot);
            if (meP != null && meP.Candidate) Line(card, T("final.you_candidate"), "pl-bad");
        }

        void FocusFinalDiscussion(VisualElement card, LineClient lc, LineView v)
        {
            var fv = v.Final;
            Big(card, F("focus.final_discussion", fv != null ? fv.Candidates.Count : 0, fv != null ? fv.FiringsTotal : 1));
            if (fv != null && fv.DefenseOrder.Count > 0) Line(card, F("focus.candidates", PlayText.Names(fv.DefenseOrder), fv.FiringsTotal), "pl-warn");
            var meP = lc.Player(lc.MySlot);
            if (meP != null && meP.Candidate) Line(card, T("final.you_candidate"), "pl-bad");
            if (LeakableCount(v) == 0) return;
            Sub(card, T("focus.optional"));
            LeakButtons(Wrap(card), v);
        }

        int FocusDefense(VisualElement card, LineClient lc, LineView v, double now)
        {
            var fv = v.Final;
            Big(card, T("hint.FinalDefense"));
            if (fv == null) return Info;
            int me = lc.MySlot;
            int at = fv.DefenseOrder.IndexOf(fv.Defender);
            for (int i = 0; i < fv.DefenseOrder.Count; i++)
            {
                int id = fv.DefenseOrder[i];
                if (id == fv.Defender)
                {
                    string name = Name(id);
                    double end = fv.DefenderEndTime;
                    Live(card, "defender" + id + ":" + end, now, t => F("focus.defender", name, UiKit.Clock(Math.Max(0, end - t))), "pl-warn", "pl-sub");
                }
                else Line(card, F(at >= 0 && i < at ? "focus.defense_done" : "focus.defense_wait", Name(id)), "pl-dim");
            }
            if (fv.Defender == me) { Line(card, T("focus.your_turn"), "pl-bad"); return Todo; }
            return Info;
        }

        void FocusVerdict(VisualElement card, LineClient lc, LineView v)
        {
            var fv = v.Final;
            if (fv == null || fv.Firings.Count == 0) { Big(card, T("hint.FinalVerdict")); return; }
            BuildFiringCard(card, fv.Firings[fv.Firings.Count - 1], lc.MySlot, true);
            if (fv.Firings.Count < fv.FiringsTotal) Line(card, F("focus.next_firing", fv.Firings.Count + 1), "pl-dim");
            else Line(card, T("focus.next_results"), "pl-dim");
        }

        /// <summary>Desk reveal: who was fired, how much was on the desk, who voted for them and what each got, what burned.</summary>
        static void BuildFiringCard(VisualElement p, LineFiringView f, int me, bool big)
        {
            var card = Box("pl-fire").AddTo(p);
            if (!big) card.AddToClassList("pl-fire-small");
            Title(F("fire.title", Name(f.Player)), "pl-fire-title").AddTo(card);
            Bold(F("fire.desk", f.Desk), "pl-fire-desk").AddTo(card);
            Line(card, F("fire.decision." + f.Decision, f.Votes));
            if (f.Recipients.Count > 0)
            {
                Line(card, F("fire.voters", f.Share));
                var row = Wrap(card);
                foreach (int id in f.Recipients) Bold(F("fire.chip", Name(id), f.Share), "pl-chip", id == me ? "pl-chip-me" : null).AddTo(row);
                if (f.Removed > 0) Line(card, F("fire.burned", f.Removed), "pl-dim");
            }
            else Line(card, F("fire.nobody", f.Desk), "pl-dim");
            if (me == f.Player) Line(card, T("fire.you_fired"), "pl-bad");
            else if (f.Recipients.Contains(me)) Line(card, F("fire.you_got", f.Share), "pl-good");
            else if (me >= 0 && big) Line(card, T("fire.you_none"), "pl-dim");
        }

        void FocusResults(VisualElement card, LineClient lc, LineView v)
        {
            var r = v.Results;
            int me = lc.MySlot;
            var buttons = Wrap(card);
            if (HostLineController.Current != null) Btn(T("results.continue"), () => HostLineController.Current?.ContinueAfterResults(), "pl-primary", "pl-big").AddTo(buttons);
            Btn(T("results.again"), () => { LineClient.I?.RequestRematch(true); TelemetryHooks.RematchRequested(); _toast.SetText(T("results.again_sent")); _toastUntil = Time.unscaledTime + 5f; }, "pl-big").AddTo(buttons);
            Btn(T("results.leave"), () => ConnectionManager.I?.Leave(), "pl-big").AddTo(buttons);
            if (r == null) { Line(card, T("results.waiting"), "pl-dim"); return; }
            var players = SortedResults(r);
            int best = players.Count > 0 && players[0].Winner ? players[0].Prim : 0;
            Title(F(r.Winners.Count > 1 ? "results.winner_many" : "results.winner_one", PlayText.Names(r.Winners), best), "pl-win").AddTo(card);
            int rank = 0, prev = int.MinValue, myRank = 0;
            for (int i = 0; i < players.Count; i++)
            {
                var pr = players[i];
                int sortPrim = pr.Fired ? -1 : pr.Prim;
                if (sortPrim != prev) { rank = i + 1; prev = sortPrim; }
                if (pr.Player == me) myRank = rank;
                var row = Box("pl-rank", pr.Winner ? "pl-rank-win" : pr.Fired ? "pl-rank-fired" : null).AddTo(card);
                Bold(pr.Fired ? "–" : "#" + rank, "pl-rank-n").AddTo(row);
                Bold(Name(pr.Player) + (pr.Player == me ? " (" + T("players.me") + ")" : ""), "pl-rank-name").AddTo(row);
                Bold(F("header.prim", pr.Prim), "pl-rank-prim").AddTo(row);
                Bold(F("players.blame", pr.Blame), "pl-rank-blame").AddTo(row);
                var tags = Box("pl-rank-tags").AddTo(row);
                if (pr.Winner) Tag(tags, T("results.winner"), "pl-tag-good");
                if (pr.Fired) Tag(tags, T("players.fired"), "pl-tag-bad");
                else if (pr.WasCandidate) Tag(tags, T("players.candidate"), "pl-tag-warn");
                if (pr.Recorded > 0 || pr.Leaked > 0) Tag(tags, F("results.recorded", pr.Recorded, pr.Leaked));
            }
            var mine = r.Players.Find(x => x.Player == me);
            if (mine != null)
                Line(card, mine.Fired ? T("results.me_fired") : mine.Winner ? F("results.me_won", mine.Prim) : F("results.me_rank", myRank, mine.Prim),
                    mine.Winner ? "pl-good" : mine.Fired ? "pl-bad" : null);
        }

        /// <summary>Winners first, then the rest by prim, fired players last.</summary>
        static List<LineWeekPlayerResult> SortedResults(LineWeekResult r)
        {
            var players = new List<LineWeekPlayerResult>(r.Players);
            players.Sort((a, b) =>
            {
                if (a.Fired != b.Fired) return a.Fired ? 1 : -1;
                if (a.Winner != b.Winner) return a.Winner ? -1 : 1;
                return b.Prim != a.Prim ? b.Prim.CompareTo(a.Prim) : a.Player.CompareTo(b.Player);
            });
            return players;
        }

        // ---- secondary information (centre, below the focus card)

        void BuildContent(VisualElement p, LineClient lc, LineView v, double now)
        {
            switch (v.Phase)
            {
                case LinePhase.Intro: BuildIntro(p, lc, v); return;
                case LinePhase.TaskPlay:
                case LinePhase.TaskReveal:
                case LinePhase.Break:
                    BuildTask(p, lc, v); return;
                case LinePhase.Results: BuildResultDetails(p, lc, v); return;
                case LinePhase.Abandoned: return;
            }
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
            if (t == null) return;
            Head(p, F("task.title", t.Number + 1, PlayText.Task(t.Kind)));
            Sub(p, T("task.rules_title"));
            Line(p, PlayText.TaskRules(t, lc.Config, v.Players.Count));
            if (!t.Revealed) return;
            if (t.Takers >= 0) Line(p, F("task.public_takers", t.Takers));
            if (t.Kind == TaskKind.WeekendShift) Line(p, F("task.public_roster", PlayText.Names(t.Roster), t.Roster.Count, t.Needed));
        }

        void BuildMeeting(VisualElement p, LineClient lc, LineView v, double now)
        {
            var mt = v.Meeting;
            var m = v.Mine;
            if (mt.Index < 2)
            {
                Head(p, F("meeting.title", mt.Index + 1));
                if (v.Phase != LinePhase.MeetingDossier && v.Phase != LinePhase.MeetingDiscussion)
                    Line(p, mt.FailedTasks.Count > 0 ? F("meeting.failed", Join(mt.FailedTasks)) : T("meeting.no_failures"), mt.FailedTasks.Count > 0 ? "pl-bad" : "pl-good");
                if (mt.VoteableCount >= 0) Line(p, mt.VoteableCount > 0 ? F("meeting.voteable", mt.VoteableCount) : T("meeting.no_vote"));
                if (mt.Roster.Count > 0) Line(p, F("meeting.roster", PlayText.Names(mt.Roster)));
            }
            if (mt.Dossier.Count > 0)
            {
                Sub(p, T(mt.Index < 2 ? "meeting.dossier" : "meeting.final_dossier"));
                foreach (var e in mt.Dossier) Line(p, "• " + PlayText.Dossier(e), e.Kind == DossierKind.Investigation || e.Kind == DossierKind.Leak ? "pl-warn" : null);
            }
            if (mt.BlameGained.Count > 0 && v.Phase != LinePhase.MeetingVerdict)
            {
                Sub(p, T("meeting.blame_gained"));
                foreach (var g in mt.BlameGained) Line(p, Name(g.Player) + ": " + F("meeting.blame_plus", g.Amount));
            }
            // leaked recordings, with a live countdown / state
            if (mt.Recordings.Count > 0)
            {
                Sub(p, T("leak.list"));
                foreach (var r in mt.Recordings)
                {
                    var pick = r;
                    string head = F("leak.line", r.Index + 1, Name(r.Leaker), Name(r.Caller), Name(r.Callee)) + " · ";
                    Live(p, "leak" + r.Index + ":" + r.PlayAt + ":" + r.Played + ":" + r.Empty, now, t => head + LeakState(pick, t));
                }
            }
            // my recordings (leak buttons are in the focus card)
            if (v.Features.RecordingsEnabled && m.Recorded.Count > 0)
            {
                Sub(p, T(v.Features.LeakOpen ? "leak.mine" : "leak.mine_closed"));
                foreach (var r in m.Recorded)
                {
                    string other = Name(r.Other), state = r.Leaked ? " · " + T("leak.leaked") : "";
                    double start = r.Start;
                    Live(p, "rec" + r.CallId + ":" + r.Leaked, now, t => F("leak.recorded", other, UiKit.Clock(Math.Max(0, t - start))) + state, r.Leaked ? "pl-dim" : null);
                }
            }
            else if (v.Features.LeakOpen && v.Features.RecordingsEnabled) Line(p, T("leak.none"), "pl-dim");
            if (v.Features.InvestigationOpen && (m.InvestigationUsed || m.InvestigationTargets.Count == 0))
            {
                Sub(p, T("investigate.title"));
                Line(p, T(m.InvestigationUsed ? "investigate.used" : "investigate.none"), "pl-dim");
            }
            else if (v.Features.InvestigationOpen && m.Prim < lc.Config.InvestigationCost) Line(p, F("investigate.poor", m.Prim), "pl-bad");
        }

        static string LeakState(LineRecordingPick r, double now)
        {
            if (r.Played) return T("leak.played");
            if (r.Empty) return T("leak.empty");
            if (now < r.PlayAt) return F("leak.queued", Math.Max(1, (int)Math.Ceiling(r.PlayAt - now)));
            if (now < r.PlayAt + Math.Max(1f, r.Seconds) + 1) return T("leak.playing");
            return T("leak.played");
        }

        static string Join(List<TaskKind> tasks)
        {
            var sb = new StringBuilder();
            foreach (var t in tasks) { if (sb.Length > 0) sb.Append(", "); sb.Append(PlayText.Task(t)); }
            return sb.ToString();
        }

        void BuildFinal(VisualElement p, LineClient lc, LineView v)
        {
            var fv = v.Final;
            if (fv.Crisis.Count > 0 && v.Phase != LinePhase.CrisisVerdict)
            {
                Sub(p, T("crisis.result"));
                foreach (var g in fv.Crisis) Line(p, Name(g.Player) + ": " + F("meeting.blame_plus", g.Amount));
            }
            if (fv.Candidates.Count > 0)
            {
                Sub(p, F("final.candidates", fv.FiringsTotal));
                foreach (int id in fv.DefenseOrder)
                {
                    var pl = lc.Player(id);
                    string line = Name(id) + " · " + F("players.blame", pl != null ? pl.Blame : 0);
                    if (pl != null && pl.Fired) line += " · " + T("players.fired");
                    Line(p, line, pl != null && pl.Fired ? "pl-dim" : null);
                }
                Line(p, T("focus.sealed_note"), "pl-dim");
            }
            // earlier firings (the latest one is the focus card in FinalVerdict)
            int shown = v.Phase == LinePhase.FinalVerdict ? fv.Firings.Count - 1 : fv.Firings.Count;
            if (shown > 0)
            {
                Sub(p, T("final.firings"));
                for (int i = 0; i < shown; i++) BuildFiringCard(p, fv.Firings[i], lc.MySlot, false);
            }
        }

        void BuildResultDetails(VisualElement p, LineClient lc, LineView v)
        {
            var r = v.Results;
            if (r == null) return;
            int me = lc.MySlot;
            Head(p, T("results.details"));
            var tabs = Wrap(p);
            Tab(tabs, TabBallots, F("results.tab.ballots", r.Ballots.Count));
            Tab(tabs, TabFirings, F("results.tab.firings", r.Firings.Count));
            Tab(tabs, TabChoices, F("results.tab.choices", r.Choices.Count));
            Tab(tabs, TabTransfers, F("results.tab.transfers", r.Transfers.Count));
            Tab(tabs, TabIncidents, F("results.tab.incidents", r.Incidents.Count));
            var body = Box("pl-tabbody").AddTo(p);
            switch (_resultsTab)
            {
                case TabFirings:
                    if (r.Firings.Count == 0) Line(body, T("common.none"), "pl-dim");
                    foreach (var f in r.Firings) BuildFiringCard(body, f, me, false);
                    break;
                case TabChoices:
                {
                    int task = -1;
                    foreach (var c in r.Choices)
                    {
                        if (c.TaskNumber != task) { task = c.TaskNumber; Sub(body, F("results.choices_task", c.TaskNumber + 1, PlayText.Task(c.Task))); }
                        bool bad = c.Choice == LineChoice.Take && c.Task != TaskKind.WeekendShift;
                        Line(body, F("results.choice_line", Name(c.Player), PlayText.Choice(c.Task, c.Choice), c.Payout), bad ? "pl-bad" : null);
                    }
                    if (r.Choices.Count == 0) Line(body, T("common.none"), "pl-dim");
                    break;
                }
                case TabTransfers:
                    if (r.Transfers.Count == 0) Line(body, T("common.none"), "pl-dim");
                    foreach (var t in r.Transfers) Line(body, F("results.transfer", Name(t.From), Name(t.To), t.Amount, PlayText.Phase(t.Phase)));
                    break;
                case TabIncidents:
                    if (r.Incidents.Count == 0) Line(body, T("common.none"), "pl-dim");
                    foreach (var e in r.Incidents) Line(body, "• " + PlayText.Dossier(e));
                    break;
                default:
                    BuildBallots(body, r);
                    break;
            }
        }

        void Tab(VisualElement row, string id, string label)
        {
            Btn(label, () => { _resultsTab = id; _dirty = true; }, _resultsTab == id ? "pl-on" : null).AddTo(row);
        }

        /// <summary>Every vote, grouped (meeting 1, meeting 2, crisis, firing 1, …), one line per voter.</summary>
        static void BuildBallots(VisualElement p, LineWeekResult r)
        {
            if (r.Ballots.Count == 0) { Line(p, T("common.none"), "pl-dim"); return; }
            string group = null;
            int voter = int.MinValue;
            var sb = new StringBuilder();
            Action flush = () => { if (sb.Length > 0) { Line(p, sb.ToString()); sb.Length = 0; } };
            foreach (var b in r.Ballots)
            {
                string g = b.Kind == BallotKind.Meeting ? F("results.ballots_meeting", b.Round + 1)
                    : b.Kind == BallotKind.Crisis ? T("results.ballots_crisis")
                    : F(b.Revote ? "results.ballots_final_revote" : "results.ballots_final", b.Round + 1);
                if (g != group) { flush(); group = g; voter = int.MinValue; Sub(p, g); }
                string target = b.SelfAssigned ? Name(b.Target) + " " + T("results.self_assigned") : b.Target >= 0 ? Name(b.Target) : T("results.abstain");
                if (b.Voter != voter) { flush(); voter = b.Voter; sb.Append(Name(b.Voter)).Append(" → ").Append(target); }
                else sb.Append(", ").Append(target);
            }
            flush();
        }

        // ---- right column

        void BuildPhone(VisualElement p, LineClient lc, LineView v, double now)
        {
            var f = v.Features;
            var m = v.Mine;
            var ph = m.Phone;
            Head(p, T("phone.title"));
            string other = Name(ph.Other);
            switch (ph.Status)
            {
                case CallStatus.Idle:
                    Line(p, T(f.CallsOpen ? "phone.idle" : "phone.closed"), "pl-dim");
                    break;
                case CallStatus.Dialing:
                {
                    double until = ph.RingUntil;
                    Live(p, "dial" + ph.Other + ":" + until, now, t => F("phone.dialing", other, UiKit.Clock(Math.Max(0, until - t))));
                    Btn(T("phone.hangup") + " (" + KeyLabel(GameInput.HangUp) + ")", () => Send(LineVerb.HangUp), "pl-danger").AddTo(p);
                    break;
                }
                case CallStatus.Ringing:
                {
                    double until = ph.RingUntil;
                    Live(p, "ring" + ph.Other + ":" + until, now, t => F("phone.ringing", other, UiKit.Clock(Math.Max(0, until - t))), "pl-warn");
                    var row = Row(p);
                    Btn(T("phone.answer") + " (" + KeyLabel(GameInput.AnswerCall) + ")", () => Send(LineVerb.Answer), "pl-primary", "pl-big").AddTo(row);
                    Btn(T("phone.decline") + " (" + KeyLabel(GameInput.HangUp) + ")", () => Send(LineVerb.Decline), "pl-danger").AddTo(row);
                    break;
                }
                case CallStatus.Connected:
                {
                    double since = ph.Since;
                    Live(p, "call" + ph.CallId + ":" + since, now, t => F("phone.connected", other, UiKit.Clock(Math.Max(0, t - since))), "pl-good");
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
                {
                    string head = Name(c.Caller) + " → " + Name(c.Callee) + " · " + T(c.Status == CallStatus.Connected ? "phone.board_talking" : "phone.board_ringing");
                    double start = c.Start;
                    Live(p, "board" + c.CallId + ":" + c.Status + ":" + start, now, t => head + " · " + UiKit.Clock(Math.Max(0, t - start)));
                }
            }
            if (m.CallLog.Count > 0)
            {
                Sub(p, T("phone.log"));
                for (int i = m.CallLog.Count - 1, n = 0; i >= 0 && n < 6; i--, n++)
                {
                    var c = m.CallLog[i];
                    string head = T(c.Outgoing ? "phone.log_out" : "phone.log_in") + " " + Name(c.Other) + " · ";
                    if (c.Connected && c.End < 0)
                    {
                        double start = c.Start;
                        Live(p, "log" + c.CallId + ":" + start, now, t => head + F("phone.log_active", UiKit.Clock(Math.Max(0, t - start))), "pl-dim");
                    }
                    else Line(p, head + (c.Connected ? UiKit.Clock(c.End - c.Start) : T("phone.log_missed." + c.EndReason)), "pl-dim");
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
                if (meP != null && meP.Fired) Line(p, T("transfer.fired"), "pl-dim");
                else if (meP != null && meP.Candidate)
                {
                    // sealed desk (docs/V5_ENGINE_SPEC.md §18): sending is disabled, receiving still works
                    Line(p, T("transfer.sealed"), "pl-bad");
                    Line(p, T("transfer.sealed_receive"), "pl-dim");
                    Btn(T("transfer.sealed_btn"), null, "pl-primary").AddTo(p).SetEnabled(false);
                }
                else
                {
                    Line(p, F("transfer.rules", lc.Config.TransferMax), "pl-dim");
                    if (v.Final != null && v.Final.Candidates.Count > 0) Line(p, T("transfer.candidates_note"), "pl-warn");
                    var targets = Wrap(p);
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
                    var send = Btn(_transferTarget >= 0 ? F("transfer.send", _transferAmount, Name(_transferTarget)) : T("transfer.pick"), () =>
                    {
                        if (_transferTarget >= 0) Send(LineVerb.Transfer, _transferTarget, _transferAmount);
                    }, "pl-primary").AddTo(p);
                    send.SetEnabled(_transferTarget >= 0 && m.Prim > 0);
                }
            }
            if (m.Transfers.Count > 0)
            {
                Sub(p, T("transfer.log"));
                foreach (var t in m.Transfers)
                    Line(p, t.From == me ? F("transfer.out", Name(t.To), t.Amount) : F("transfer.in", Name(t.From), t.Amount), t.From == me ? "pl-bad" : "pl-good");
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
                if (msg.Kind == MessageKind.MeetingChat) who = F("msg.chat_from", Name(msg.From));
                else if (msg.Kind == MessageKind.System) who = T("msg.system");
                else if (msg.From == me) who = F("msg.text_to", Name(msg.To));
                else who = F("msg.text_from", Name(msg.From));
                Line(p, who + " " + msg.Text, msg.Kind == MessageKind.MeetingChat ? null : "pl-private");
            }
        }

        void BuildComposeTargets(VisualElement p, LineClient lc, LineView v)
        {
            var f = v.Features;
            var m = v.Mine;
            int me = lc.MySlot;
            int target = _msgTarget == -2 && f.MeetingChatOpen ? -1 : _msgTarget;
            _composeInfo.SetText(target == -1 ? F("msg.to_all", m.MeetingChatLeft) : target >= 0 ? F("msg.to", Name(target), m.TextsLeft) : T("msg.pick_target"));
            var row = Wrap(p);
            if (f.MeetingChatOpen) Btn(T("msg.all"), () => { _msgTarget = -1; _dirty = true; }, target == -1 ? "pl-on" : null).AddTo(row);
            if (f.TextsOpen)
                foreach (var pl in v.Players)
                {
                    if (pl.Id == me) continue;
                    int id = pl.Id;
                    Btn(pl.Name, () => { _msgTarget = id; _dirty = true; }, target == id ? "pl-on" : null).AddTo(row);
                }
        }

        // ================================================================ section with content diff and live labels

        sealed class LiveLabel
        {
            public string Key;
            public Action<Label, double> Apply;
        }

        /// <summary>
        /// A rebuildable block. <see cref="Update"/> builds a fresh subtree and swaps it in only when its rendered
        /// content (element types, classes, texts, enabled state) differs from what is shown. Live labels count only
        /// by their key; <see cref="TickLive"/> refreshes them in place.
        /// </summary>
        sealed class Section
        {
            readonly VisualElement _holder;
            readonly List<Label> _live = new List<Label>();
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
                _live.Clear();
                Collect(next, _live);
            }

            public void TickLive(double now)
            {
                for (int i = 0; i < _live.Count; i++)
                {
                    var l = _live[i];
                    if (l.userData is LiveLabel ll) ll.Apply(l, now);
                }
            }

            static void Collect(VisualElement e, List<Label> into)
            {
                if (e is Label l && l.userData is LiveLabel) into.Add(l);
                foreach (var child in e.Children()) Collect(child, into);
            }

            static void Dump(VisualElement e, StringBuilder sb)
            {
                if (e.userData is LiveLabel ll) { sb.Append("<live:").Append(ll.Key).Append('>'); return; }
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
