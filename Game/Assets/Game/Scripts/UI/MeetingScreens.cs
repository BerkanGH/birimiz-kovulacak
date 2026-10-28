using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using EventType = BirimizKovulacak.Core.EventType;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// HR meeting: projector slides of the dossier (evidence cards incl. e-mail logs, CCTV footage with a floor plan,
    /// call logs), people with accuse / vote actions, defense spotlight, footage request, note presentation and the
    /// verdict breakdown (vote / risk / performance / favour discount).
    /// </summary>
    public sealed class MeetingScreen : UiKit.Screen
    {
        enum Slide { Evidence = 0, Footage = 1, Observations = 2, Log = 3 }

        readonly Label _phase, _timer, _instruction, _slideTitle;
        readonly VisualElement _slideBody, _side, _footagePanel;
        readonly UiKit.TabBar _slides;
        readonly ScrollView _slideScroll;
        // footage request (persistent so dragging the slider survives view updates)
        readonly VisualElement _camList;
        readonly SliderInt _footageSlider;
        readonly Label _footageTime, _footageLeft;
        readonly Button _footageSend;
        readonly List<Button> _camButtons = new List<Button>();
        readonly Dictionary<int, VisualElement> _footageCache = new Dictionary<int, VisualElement>();
        int _camIndex;
        Slide _slide = Slide.Evidence;
        int _lastVersion = -1, _lastSec = -1, _cacheMatch = -1, _lastFootageCount;
        MatchPhase _lastPhase;
        bool _dirty;

        public MeetingScreen(VisualElement parent) : base(parent)
        {
            var top = Box("meeting-top").AddTo(Root);
            var pill = Box("phase-pill").AddTo(top);
            Icon("ballot", "icon-m").AddTo(pill);
            var pc = Box("phase-col").AddTo(pill);
            _phase = Bold("", "phase-name").AddTo(pc);
            _timer = Title("", "timer").AddTo(pc);
            _instruction = Bold("", "meeting-instr").AddTo(top);

            var body = Box("meeting-body").AddTo(Root);
            var proj = Box("projector").AddTo(body);
            var frame = Box("projector-frame").AddTo(proj);
            if (!UiArt.Apply(frame, "meeting/projector_frame")) frame.AddToClassList("projector-fallback");
            var screen = Box("projector-screen").AddTo(frame);
            KeepAspect(proj, frame, 1600f / 900f);
            var head = Box("slide-head").AddTo(screen);
            var folder = Box("icon", "icon-m", "gap-m").AddTo(head);
            if (!UiArt.Apply(folder, "meeting/evidence_folder")) SetIcon(folder, "notes");
            _slideTitle = Title("", "slide-title").AddTo(head);
            _slides = new UiKit.TabBar(head, id => { _slide = (Slide)id; _dirty = true; _slideScroll.scrollOffset = Vector2.zero; }, "slide-nav");
            _slides.Add((int)Slide.Evidence, Loc.T("meeting.slide.Evidence"));
            _slides.Add((int)Slide.Footage, Loc.T("meeting.slide.Footage"));
            _slides.Add((int)Slide.Observations, Loc.T("meeting.slide.Observations"));
            _slides.Add((int)Slide.Log, Loc.T("meeting.slide.Log"));
            _slideScroll = new ScrollView().Cls("slide-body").AddTo(screen);
            _slideBody = _slideScroll.contentContainer;

            var sidePanel = Panel("panel", "side").AddTo(body);
            var sideScroll = new ScrollView().Cls("grow").AddTo(sidePanel);
            _side = Box("col").AddTo(sideScroll.contentContainer);

            // footage request panel
            _footagePanel = Box("footage-panel").AddTo(sideScroll.contentContainer);
            Section(_footagePanel, Loc.T("footage.request_title"), "hr_footage");
            _footageLeft = Text("", "t-small", "t-light").AddTo(_footagePanel);
            _camList = Box("cam-list").AddTo(_footagePanel);
            var layout = GameConfig.Layout;
            for (int i = 0; i < layout.Cameras.Count; i++)
            {
                int idx = i;
                var b = Btn(Loc.Camera(layout.Cameras[i].Id), () => { _camIndex = idx; RefreshCamButtons(); }, "cam-btn", "btn-small").AddTo(_camList);
                _camButtons.Add(b);
            }
            _footageTime = Title("00:00", "footage-time").AddTo(_footagePanel);
            _footageSlider = new SliderInt(Loc.T("footage.time"), 0, 300) { value = 60 }.Cls("slider").AddTo(_footagePanel);
            _footageSlider.RegisterValueChangedCallback(e => _footageTime.text = Loc.MatchSeconds(e.newValue));
            _footageSend = IconBtn("footage", Loc.T("footage.request"), SendFootage, "btn-primary").AddTo(_footagePanel);
            _footagePanel.Show(false);
            RefreshCamButtons();

            if (ClientState.I != null) ClientState.I.EventReceived += OnEvent;
        }

        void OnEvent(GameEvent e)
        {
            if (!Visible) return;
            if (e.Type == EventType.FootageShown) { _slides.Select((int)Slide.Footage, false); _slide = Slide.Footage; _dirty = true; }
        }

        void RefreshCamButtons()
        {
            for (int i = 0; i < _camButtons.Count; i++)
            {
                _camButtons[i].EnableInClassList("btn-selected", i == _camIndex);
                _camButtons[i].EnableInClassList("btn-secondary", i != _camIndex);
            }
        }

        void SendFootage()
        {
            var layout = GameConfig.Layout;
            if (_camIndex < 0 || _camIndex >= layout.Cameras.Count) return;
            ClientState.I.Send(ActionVerb.RequestFootage, layout.Cameras[_camIndex].Id, _footageSlider.value);
        }

        protected override void OnShow()
        {
            _lastVersion = -1;
            // Evidence ids restart every match (and match ids every session): never reuse an old footage card.
            _footageCache.Clear();
            _lastFootageCount = 0;
            _cacheMatch = -1;
            _slide = Slide.Evidence;
            _slides.Select((int)Slide.Evidence, false);
        }
        protected override void OnHide() { SetCctv(false); }

        bool _cctv;
        void SetCctv(bool on)
        {
            if (on == _cctv) return;
            _cctv = on;
            ScreenFx.SetCctv(on);
        }

        public override void Tick()
        {
            if (!Visible) return;
            var cs = ClientState.I;
            var v = cs.View;
            if (v == null) return;
            if (v.Phase == MatchPhase.MeetingDiscussion && MatchClock.MeetingMatchId != v.MatchId)
            {
                MatchClock.MeetingMatchId = v.MatchId;
                MatchClock.MeetingStart = v.PhaseStartTime;
            }
            if (_cacheMatch != v.MatchId) { _cacheMatch = v.MatchId; _footageCache.Clear(); _lastFootageCount = 0; }
            int sec = (int)System.Math.Ceiling(System.Math.Max(0, v.PhaseEndTime - cs.Now));
            if (sec != _lastSec) { _lastSec = sec; _timer.text = ClockInt(sec); _timer.EnableInClassList("timer-urgent", sec <= 5); }
            SetCctv(_slide == Slide.Footage && _lastFootageCount > 0);
            if (v.StateVersion == _lastVersion && v.Phase == _lastPhase && !_dirty) return;
            bool phaseChanged = v.Phase != _lastPhase;
            _lastVersion = v.StateVersion;
            _lastPhase = v.Phase;
            _dirty = false;
            _phase.text = Loc.Phase(v.Phase);
            _instruction.text = Instruction(v);
            int footageCount = 0;
            foreach (var e in v.Evidence) if (e.Kind == EvidenceKind.CctvFootage) footageCount++;
            _slides.SetTabVisible((int)Slide.Footage, footageCount > 0);
            if (footageCount > _lastFootageCount) { _slide = Slide.Footage; _slides.Select((int)Slide.Footage, false); }
            _lastFootageCount = footageCount;
            if (phaseChanged && v.Phase == MatchPhase.MeetingVerdict) { _slide = Slide.Evidence; _slides.Select((int)Slide.Evidence, false); }
            BuildSlide(v);
            BuildSide(v);
            UpdateFootagePanel(v);
        }

        static string Instruction(PlayerView v)
        {
            switch (v.Phase)
            {
                case MatchPhase.MeetingDiscussion: return Loc.T("meeting.instr.discussion");
                case MatchPhase.MeetingDefense1:
                case MatchPhase.MeetingDefense2:
                    return v.Meeting.Speaker == ClientState.I.MySlot ? Loc.T("meeting.instr.defense_you") : Loc.F("meeting.instr.defense", Loc.PlayerName(v, v.Meeting.Speaker));
                case MatchPhase.MeetingVote: return Loc.T("meeting.instr.vote");
                case MatchPhase.MeetingVerdict: return Loc.T("meeting.instr.verdict");
                default: return "";
            }
        }

        // ------------------------------------------------------------------ slides

        void BuildSlide(PlayerView v)
        {
            _slideBody.Clear();
            _slideTitle.text = Loc.T("meeting.slide_title." + _slide);
            bool informantScan = v.Me.Role == Role.Informant && v.Phase == MatchPhase.MeetingDiscussion;
            switch (_slide)
            {
                case Slide.Evidence:
                {
                    var grid = Box("ev-grid").AddTo(_slideBody);
                    int n = 0;
                    for (int i = v.Evidence.Count - 1; i >= 0; i--)
                    {
                        if (v.Evidence[i].Kind == EvidenceKind.CctvFootage) continue;
                        PhonePanel.EvidenceCard(grid, v.Evidence[i], v, informantScan);
                        n++;
                    }
                    if (n == 0) Empty(_slideBody, Loc.T("phone.no_evidence"));
                    break;
                }
                case Slide.Footage:
                {
                    int n = 0;
                    for (int i = v.Evidence.Count - 1; i >= 0; i--)
                    {
                        var e = v.Evidence[i];
                        if (e.Kind != EvidenceKind.CctvFootage) continue;
                        n++;
                        VisualElement block;
                        if (!_footageCache.TryGetValue(e.Id, out block))
                        {
                            block = Box("ev-card", "ev-system");
                            var head = Box("ev-head").AddTo(block);
                            Icon("footage").AddTo(head);
                            Bold(Describe.Evidence(e, v), "ev-title").AddTo(head);
                            var row = Box("row-top").AddTo(block);
                            FootageView.Build(row, e, v, 560, 350);
                            var list = Box("col", "grow").AddTo(row);
                            list.style.marginLeft = 12;
                            if (e.Sightings.Count == 0 && !e.NoRecording) Empty(list, Loc.T("footage.nobody"));
                            foreach (var s in e.Sightings) Text(Describe.Sighting(s, v), "t-small").AddTo(list);
                            _footageCache[e.Id] = block;
                        }
                        block.AddTo(_slideBody);
                    }
                    if (n == 0) Empty(_slideBody, Loc.T("footage.none_yet"));
                    break;
                }
                case Slide.Observations:
                {
                    int n = 0;
                    for (int i = v.Observations.Count - 1; i >= 0; i--)
                    {
                        var o = v.Observations[i];
                        if (o.Kind == ObservationKind.SuspiciousItemAtDesk || o.Kind == ObservationKind.TipFound) continue;
                        PhonePanel.ObservationRow(_slideBody, o, v);
                        n++;
                    }
                    if (n == 0) Empty(_slideBody, Loc.T("phone.no_observations"));
                    break;
                }
                case Slide.Log:
                {
                    int shown = 0;
                    for (int i = v.Log.Count - 1; i >= 0 && shown < 40; i--)
                    {
                        var l = v.Log[i];
                        if (!LogWorthy(l.Kind)) continue;
                        var ge = new GameEvent { Type = l.Kind, Time = l.Time, Actor = l.Actor, TargetId = l.TargetId, IntArg = l.IntArg, StrArg = l.StrArg };
                        Hud.ToastKind k;
                        string s = Describe.Event(ge, v, out k);
                        if (s == null) continue;
                        Text(Loc.Time(l.Time, v) + "  " + s, "list-row", "list-row-text").AddTo(_slideBody);
                        shown++;
                    }
                    if (shown == 0) Empty(_slideBody, Loc.T("meeting.log_empty"));
                    break;
                }
            }
        }

        static bool LogWorthy(EventType k)
        {
            switch (k)
            {
                case EventType.WorkstationLocked: case EventType.PhoneMissed: case EventType.TaskCompleted: case EventType.EmailSent:
                case EventType.InternetDown: case EventType.InternetRestored: case EventType.PowerDown: case EventType.PowerRestored:
                case EventType.PlugPulled: case EventType.PlugRestored: case EventType.ManagerTipReceived: case EventType.SurpriseInspection:
                    return true;
                default: return false;
            }
        }

        // ------------------------------------------------------------------ side

        void BuildSide(PlayerView v)
        {
            _side.Clear();
            var me = v.Me;
            int my = ClientState.I.MySlot;
            var m = v.Meeting;

            if (v.Phase == MatchPhase.MeetingVerdict && m.Verdict != null)
            {
                BuildVerdict(_side, v, m.Verdict);
                return;
            }

            bool defense = v.Phase == MatchPhase.MeetingDefense1 || v.Phase == MatchPhase.MeetingDefense2;
            if (defense && m.Speaker >= 0)
            {
                var sp = Box("spotlight").AddTo(_side);
                Badge(m.Speaker, "badge-l").AddTo(sp);
                var c = Box("col", "grow").AddTo(sp);
                Bold(Loc.T("meeting.spotlight"), "spotlight-sub").AddTo(c);
                Title(Loc.PlayerName(v, m.Speaker), "spotlight-name").AddTo(c);
                Text(m.Speaker == my ? Loc.T("meeting.spotlight_you") : Loc.T("meeting.spotlight_listen"), "spotlight-sub").AddTo(c);
            }

            Section(_side, Loc.T("meeting.people"), "contacts");
            bool favor = GameInfo.FavorMatters(v);
            foreach (var p in v.Players)
            {
                int pid = p.Id;
                var row = Box("person").AddTo(_side);
                if (m.Speaker == p.Id) row.AddToClassList("person-speaking");
                if (m.Shortlist.Contains(p.Id)) row.AddToClassList("person-shortlisted");
                if (me.Ballot == p.Id && me.HasVoted) row.AddToClassList("person-voted");
                Badge(p.Id, "badge-m").AddTo(row);
                var col = Box("col", "grow").AddTo(row);
                var nameRow = Box("row").AddTo(col);
                Bold(p.Name + (p.Id == my ? " " + Loc.T("hud.you") : ""), "person-name").AddTo(nameRow);
                if (m.Shortlist.Contains(p.Id)) Tag(Loc.T("meeting.shortlisted"), "tag-red").AddTo(nameRow);
                if (v.Favourite == p.Id) Tag(Loc.T("meeting.favourite"), "tag-sticky").AddTo(nameRow);
                if (!p.Connected) Tag(Loc.T("meeting.offline"), "tag-grey").AddTo(nameRow);
                string stats = Loc.F("meeting.stats", p.Performance.ToString("0"), p.AccusationsReceived) + (favor ? " · " + Loc.F("meeting.favor", p.Favor.ToString("0.#")) : "");
                Text(stats, "person-sub").AddTo(col);
                string accusers = Accusers(v, p.Id);
                if (accusers != null) Text(Loc.F("meeting.accused_by", accusers), "person-sub", "t-bad").AddTo(col);
                var pips = Pips(p.SuspicionLevel).Cls("on-paper").AddTo(row);

                if (v.Phase == MatchPhase.MeetingDiscussion && p.Id != my && !me.HasAccused)
                    IconBtn("accuse", Loc.T("meeting.accuse"), () => ConfirmAccuse(pid), "btn-danger", "btn-small").AddTo(row);
                if (v.Phase == MatchPhase.MeetingVote && m.Shortlist.Contains(p.Id) && p.Id != my)
                {
                    bool mine = me.HasVoted && me.Ballot == p.Id;
                    var b = IconBtn("ballot", mine ? Loc.T("meeting.voted_for") : Loc.T("meeting.vote_fire"), () => ClientState.I.Send(ActionVerb.CastBallot, "", pid), "btn-small", mine ? "btn-selected" : "btn-danger").AddTo(row);
                    b.SetEnabled(!me.HasVoted);
                }
            }

            if (me.HasAccused) Bold(Loc.F("meeting.you_accused", Loc.PlayerName(v, me.AccusedPlayer)), "t-accent", "mt-s").AddTo(_side);
            else if (v.Phase == MatchPhase.MeetingDiscussion) Text(Loc.T("meeting.accuse_help"), "t-small", "t-muted", "mt-s").AddTo(_side);

            if (v.Phase == MatchPhase.MeetingVote)
            {
                var row = Box("row", "mt-s").AddTo(_side);
                var b = Btn(Loc.T("meeting.abstain"), () => ClientState.I.Send(ActionVerb.CastBallot, "", PlayerIds.None), "btn-small", "btn-ghost").AddTo(row);
                b.SetEnabled(!me.HasVoted);
                int connected = 0;
                foreach (var p in v.Players) if (p.Connected) connected++;
                Text(Loc.F("meeting.ballots", m.BallotsCast, connected), "t-muted", "grow", "t-right").AddTo(row);
                if (me.HasVoted) Bold(me.Ballot >= 0 ? Loc.F("meeting.ballot_done", Loc.PlayerName(v, me.Ballot)) : Loc.T("meeting.ballot_abstained"), "t-good").AddTo(_side);
                if (m.Shortlist.Contains(my)) Text(Loc.T("meeting.you_on_shortlist"), "t-small", "t-bad").AddTo(_side);
            }

            if (GameInfo.PresentPhase(v.Phase))
            {
                Section(_side, Loc.T("meeting.my_notes"), "notes");
                Text(Loc.T("meeting.present_help"), "t-small", "t-muted").AddTo(_side);
                int presented = 0;
                foreach (var n in me.Notes) if (n.Presented) presented++;
                int unpresented = 0;
                for (int i = me.Notes.Count - 1; i >= 0; i--)
                {
                    var n = me.Notes[i];
                    if (n.Presented) continue;
                    unpresented++;
                    var card = PhonePanel.NoteCard(_side, n, v, presented < 2 ? (System.Action<int>)(id => ClientState.I.Send(ActionVerb.PresentNote, "", id)) : null);
                }
                if (unpresented == 0) Empty(_side, Loc.T("phone.no_notes"));
                if (presented >= 2) Text(Loc.T("meeting.present_limit"), "t-small", "t-muted").AddTo(_side);
            }
            Text(Loc.T(UiServices.Voice.Available ? "meeting.voice_hint" : "meeting.discord_hint"), "t-small", "t-muted", "mt-m").AddTo(_side);
        }

        static string Accusers(PlayerView v, int accused)
        {
            System.Text.StringBuilder sb = null;
            foreach (var a in v.Meeting.Accusations)
            {
                if (a.Accused != accused) continue;
                if (sb == null) sb = new System.Text.StringBuilder();
                else sb.Append(", ");
                sb.Append(Loc.PlayerName(v, a.Accuser));
            }
            return sb != null ? sb.ToString() : null;
        }

        static void ConfirmAccuse(int player)
        {
            var v = ClientState.I.View;
            ConfirmDialog.Show(Loc.T("meeting.accuse_title"), Loc.F("meeting.accuse_confirm", Loc.PlayerName(v, player)), Loc.T("meeting.accuse_ok"),
                () => ClientState.I.Send(ActionVerb.Accuse, "", player), true);
        }

        void UpdateFootagePanel(PlayerView v)
        {
            int my = ClientState.I.MySlot;
            bool defenseMine = (v.Phase == MatchPhase.MeetingDefense1 || v.Phase == MatchPhase.MeetingDefense2) && v.Meeting.Speaker == my;
            bool informant = v.Me.Role == Role.Informant && GameInfo.PresentPhase(v.Phase);
            bool allowed = v.Features.Footage && v.Me.FootageLeft > 0 && (defenseMine || informant);
            _footagePanel.Show(allowed);
            if (!allowed) return;
            _footagePanel.SendToBack();
            _footageLeft.text = Loc.F("footage.left", v.Me.FootageLeft);
            int max = GameInfo.FootageMaxSecond(v);
            if (_footageSlider.highValue != max)
            {
                _footageSlider.highValue = max;
                if (_footageSlider.value > max) _footageSlider.value = max;
            }
            _footageTime.text = Loc.MatchSeconds(_footageSlider.value);
        }

        // ------------------------------------------------------------------ verdict

        static void BuildVerdict(VisualElement parent, PlayerView v, VerdictView vd)
        {
            Title(Loc.T("meeting.verdict_title"), "h2").AddTo(parent);
            var who = Box("row").AddTo(parent);
            Badge(vd.FiredPlayer, "badge-xl").AddTo(who);
            var wc = Box("col", "grow").AddTo(who);
            wc.style.marginLeft = 14;
            Bold(Loc.T("meeting.fired_is"), "t-muted").AddTo(wc);
            Title(Loc.Upper(Loc.PlayerName(v, vd.FiredPlayer)), "verdict-name").AddTo(wc);
            Text(Loc.F("meeting.formula", (vd.VoteWeight * 100).ToString("0"), (vd.RiskWeight * 100).ToString("0"), (vd.PerformanceWeight * 100).ToString("0")), "t-small", "t-muted").AddTo(parent);

            float max = 0.01f;
            foreach (var c in vd.Candidates)
            {
                float total = vd.VoteWeight * c.VoteShare + vd.RiskWeight * c.RiskNormalized + vd.PerformanceWeight * c.PerformanceDeficit;
                if (total > max) max = total;
            }
            foreach (var c in vd.Candidates)
            {
                var row = Box("score-row").AddTo(parent);
                if (c.Player == vd.FiredPlayer) row.AddToClassList("score-fired");
                var head = Box("score-head").AddTo(row);
                Badge(c.Player, "badge-s").AddTo(head);
                Bold(Loc.PlayerName(v, c.Player), "score-name").AddTo(head);
                Title(c.Score.ToString("0.00"), "score-total").AddTo(head);
                var bar = Box("score-bar").AddTo(row);
                Seg(bar, "seg-vote", vd.VoteWeight * c.VoteShare / max);
                Seg(bar, "seg-risk", vd.RiskWeight * c.RiskNormalized / max);
                Seg(bar, "seg-perf", vd.PerformanceWeight * c.PerformanceDeficit / max);
                string detail = Loc.F("meeting.score_detail", c.Votes, (c.VoteShare * 100).ToString("0"), c.ObservedRisk.ToString("0.#"), c.Performance.ToString("0"));
                if (c.FavorDiscount > 0.001f) detail += " · " + Loc.F("meeting.favor_discount", c.FavorDiscount.ToString("0.00"));
                Text(detail, "score-detail").AddTo(row);
            }
            var legend = Box("legend").AddTo(parent);
            Legend(legend, "seg-vote", Loc.T("meeting.col_votes"));
            Legend(legend, "seg-risk", Loc.T("meeting.col_risk"));
            Legend(legend, "seg-perf", Loc.T("meeting.col_perf"));
            if (vd.TieBreakUsed != TieBreak.None) Bold(Loc.T("meeting.tiebreak." + vd.TieBreakUsed), "t-accent", "mt-s").AddTo(parent);
            if (vd.KeyEvidence.Count > 0 || vd.KeyObservations.Count > 0)
            {
                Section(parent, Loc.T("meeting.key_reasons"), "warning");
                foreach (int id in vd.KeyEvidence)
                    foreach (var e in v.Evidence) if (e.Id == id) Text("• " + Describe.Evidence(e, v), "t-small").AddTo(parent);
                foreach (int id in vd.KeyObservations)
                    foreach (var o in v.Observations) if (o.Id == id) Text("• " + Describe.Observation(o, v), "t-small").AddTo(parent);
            }
        }

        static void Seg(VisualElement bar, string cls, float fraction)
        {
            var s = Box("seg", cls).AddTo(bar);
            s.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
        }

        static void Legend(VisualElement parent, string cls, string text)
        {
            var i = Box("legend-item").AddTo(parent);
            Box("legend-swatch", cls).AddTo(i);
            Text(text, "t-tiny").AddTo(i);
        }
    }
}
