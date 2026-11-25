using System;
using System.Collections.Generic;
using System.Text;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;
using static BirimizKovulacak.Game.PlayBlocks;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 play UI: one screen for every match phase. Header (phase, timer, my prim, voice mode), left column (players:
    /// suç board, call / open the file / transfer / text shortcuts), centre column (a "what do you do now?"
    /// focus card with this phase's main actions — task choice, the files I hold, ballots, defence order, the desk reveal, the
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
        readonly PlaySection _players, _focus, _content, _phone, _transfer, _events, _messages, _composeTargets;
        readonly PlaySection[] _sections;
        readonly PlayState _st = new PlayState();
        readonly VisualElement _tapeHost;
        readonly TapeDeck _tape;
        readonly TextField _msgField;
        readonly Label _composeInfo;
        readonly List<string> _feed = new List<string>();
        bool _dirty = true, _bound;
        float _toastUntil, _nextLive;
        int _week = int.MinValue;
        bool _ringing;
        string _resultsTab = TabBallots;

        const string TabFirings = "firings", TabBallots = "ballots", TabTransfers = "transfers", TabChoices = "choices", TabIncidents = "incidents";
        /// <summary>UI mocks (Dev/LineMock.cs): switch the results details to this tab ("ballots", "firings", "choices", "transfers", "incidents").</summary>
        public static string MockResultsTab;

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

            _players = new PlaySection(left);
            _focus = new PlaySection(center);
            _tapeHost = Box("pl-tape").AddTo(center);
            _tape = new TapeDeck(_tapeHost);
            _tapeHost.Show(false);
            _content = new PlaySection(center);
            _phone = new PlaySection(right);
            _transfer = new PlaySection(right);
            _events = new PlaySection(right);
            _messages = new PlaySection(right);

            var compose = Box("pl-section", "pl-compose").AddTo(right);
            _composeInfo = Text("", "pl-text", "pl-dim").AddTo(compose);
            _composeTargets = new PlaySection(compose);
            var row = Box("pl-row").AddTo(compose);
            _msgField = new TextField { maxLength = 200 }.Cls("pl-field", "grow").AddTo(row);
            _msgField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { SendMessage(); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            Btn(T("msg.send"), SendMessage, "pl-primary").AddTo(row);
            _sections = new[] { _players, _focus, _content, _phone, _transfer, _events, _messages, _composeTargets };
            _st.Dirty = () => _dirty = true;
            _st.Toast = ShowToast;
            _st.FocusMessageField = () => _msgField.Focus();
        }

        void ShowToast(string text)
        {
            _toast.SetText(text);
            _toastUntil = Time.unscaledTime + 4f;
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
            if (e.Type == LineEventType.TransferReceived || e.Type == LineEventType.FileOpened) Sfx.Play("notify");
            if (e.Type == LineEventType.FileNotice || e.Type == LineEventType.FileShredded && lc != null && e.Actor != lc.MySlot) Sfx.Play("shred", 0.6f);
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
                _st.ResetWeek(); _resultsTab = TabBallots;
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
                _transfer.Update(p => PlayBlocks.BuildTransfer(p, lc, v, _st));
                _events.Update(BuildEvents);
                _messages.Update(p => PlayBlocks.BuildMessages(p, lc, v));
                _composeTargets.Update(p => PlayBlocks.BuildComposeTargets(p, lc, v, _st, _composeInfo));
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
            _phase.SetText(PlayText.PhaseTitle(v));
            double left = Math.Max(0, v.PhaseEndTime - now);
            _timer.SetText(v.Phase == LinePhase.Abandoned ? "" : UiKit.Clock(left));
            var m = v.Mine;
            _prim.SetText(F("header.my_prim", m.Prim));
            var sb = new StringBuilder();
            sb.Append(lc.NameOf(lc.MySlot));
            if (v.Features.RecordingsEnabled) sb.Append(" · ").Append(F("header.recorder", m.RecorderUsesLeft));
            int held = PlayBlocks.HeldFiles(v, lc.MySlot);
            if (held > 0) sb.Append(" · ").Append(F("header.files", held));
            string counter = PlayText.FilesCounter(v);
            if (counter != null) sb.Append(" · ").Append(counter);
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

        void SendMessage()
        {
            var lc = LineClient.I;
            var v = lc != null ? lc.View : null;
            if (!PlayBlocks.SendMessage(v, _st, _msgField.value)) return;
            _msgField.SetValueWithoutNotify("");
            _msgField.Focus();
        }

        // ================================================================ builders

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
                // v5.3: the shortcut to "Dosyalarım" for a colleague I hold a file on (the confirmation is asked there)
                var file = PlayBlocks.HeldFile(v, me, id);
                if (file != null) Btn(T("act.file_open"), () => _st.AskFile(file, 1), "pl-danger").AddTo(acts);
                if (canSend) Btn(T("act.transfer"), () => { _st.TransferTarget = id; _dirty = true; }, _st.TransferTarget == id ? "pl-on" : null).AddTo(acts);
                if (f.TextsOpen) Btn(T("act.text"), () => { _st.MsgTarget = id; _dirty = true; _msgField.Focus(); }, _st.MsgTarget == id ? "pl-on" : null).AddTo(acts);
            }
        }

        // ---- focus card (top of the centre column): what to do now, with this phase's main actions

        void BuildFocus(VisualElement p, LineClient lc, LineView v, double now)
        {
            if (v.Phase == LinePhase.None) return;
            var card = Box("pl-focus").AddTo(p);
            var head = Row(card);
            string kicker = v.Phase == LinePhase.Results ? "focus.kicker_results" : v.Phase == LinePhase.FinalVerdict ? "focus.kicker_verdict" : "focus.kicker";
            Bold(T(kicker), "pl-focus-kicker", "grow").AddTo(head);
            int page = PlayBlocks.HowToPage(v);
            if (page >= 0) Btn(T("focus.rules"), () => HowToPlayScreen.OpenAt(page), "pl-small").AddTo(head);
            int state = Info;
            switch (v.Phase)
            {
                case LinePhase.Intro:
                    Big(card, T("hint.Intro"));
                    Line(card, F("focus.intro_goal", lc.Config.StartPrim));
                    PlayBlocks.AgendaChips(card, v);
                    break;
                case LinePhase.TaskPlay: state = PlayBlocks.FocusTask(card, lc, v); break;
                case LinePhase.TaskReveal:
                case LinePhase.Break: state = PlayBlocks.FocusReveal(card, lc, v); break;
                case LinePhase.MeetingDossier:
                case LinePhase.MeetingDiscussion: FocusMeeting(card, lc, v); break;
                case LinePhase.MeetingVote:
                case LinePhase.CrisisVote:
                case LinePhase.FinalVote: state = FocusVote(card, lc, v, now); break;
                case LinePhase.MeetingVerdict: FocusMeetingVerdict(card, v); break;
                case LinePhase.CrisisCalls: PlayBlocks.CrisisCalls(card, lc, v, "focus.crisis_how"); break;
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
            // v5.3 "Dosyalarım": the files I hold (open / shred, each with a confirmation) and the files on me
            if (v.Phase < LinePhase.Results && v.Mine.Files != null && v.Mine.Files.Count > 0)
            {
                var files = Box("pl-focus").AddTo(p);
                Bold(Loc.Upper(T("files.title")), "pl-focus-kicker").AddTo(files);
                if (PlayBlocks.BuildFiles(files, lc, v, _st) == Todo) files.AddToClassList("pl-focus-todo");
            }
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
                Big(card, mt.Index == 0 ? T("focus.meeting1") : F("focus.meeting2", c.ShiftFailSkipBlame));
                Line(card, F("meeting.failed", JoinTasks(mt.FailedTasks)), "pl-bad");
                Line(card, mt.VoteableCount == 0 ? PlayText.NoVote(mt, c) : T("focus.meeting_vote_next"));
            }
            if (LeakableCount(v) == 0) return;
            Sub(card, T("focus.optional"));
            LeakButtons(Wrap(card), v);
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
            if (mt == null || !mt.BlameGained.Exists(g => g.Amount > 0)) { Line(card, T("focus.no_blame"), "pl-dim"); return; }
            var row = Wrap(card);
            foreach (var g in mt.BlameGained)
                if (g.Amount > 0) Bold(Name(g.Player) + " " + F("meeting.blame_plus", g.Amount), "pl-chip", "pl-chip-warn").AddTo(row);
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
            else { Line(card, T("final.no_candidates"), "pl-good"); return; }
            Line(card, T("focus.sealed_note"));
            var meP = lc.Player(lc.MySlot);
            if (meP != null && meP.Candidate) Line(card, T("final.you_candidate"), "pl-bad");
            // v5.2: a candidate declares their prim
            PlayBlocks.DeclareRow(card, lc, v, _st);
        }

        void FocusFinalDiscussion(VisualElement card, LineClient lc, LineView v)
        {
            var fv = v.Final;
            Big(card, F("focus.final_discussion", fv != null ? fv.Candidates.Count : 0, fv != null ? fv.FiringsTotal : 1));
            if (fv != null && fv.DefenseOrder.Count > 0) Line(card, F("focus.candidates", PlayText.Names(fv.DefenseOrder), fv.FiringsTotal), "pl-warn");
            var meP = lc.Player(lc.MySlot);
            if (meP != null && meP.Candidate) Line(card, T("final.you_candidate"), "pl-bad");
            PlayBlocks.DeclareRow(card, lc, v, _st);
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

        // ---- secondary information (centre, below the focus card)

        void BuildContent(VisualElement p, LineClient lc, LineView v, double now)
        {
            switch (v.Phase)
            {
                case LinePhase.Intro: PlayBlocks.BuildIntro(p, lc, v); return;
                case LinePhase.TaskPlay:
                case LinePhase.TaskReveal:
                case LinePhase.Break:
                    PlayBlocks.BuildTask(p, lc, v); return;
                case LinePhase.Results: BuildResultDetails(p, lc, v); return;
                case LinePhase.Abandoned: return;
            }
            if (v.Final != null) BuildFinalInfo(p, lc, v);
            if (v.Meeting != null && (LineClient.IsMeetingPhase(v.Phase) || v.Phase == LinePhase.CrisisCalls)) BuildMeetingInfo(p, lc, v, now);
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
            PlayBlocks.BuildCallLog(p, v, now, 6);
        }

        void BuildEvents(VisualElement p)
        {
            if (_feed.Count == 0) return;
            Head(p, T("events.title"));
            for (int i = _feed.Count - 1; i >= 0; i--) Line(p, _feed[i], "pl-dim");
        }
    }
}
