using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Task app (v4): ONE renderer for all twelve tasks. It draws the header (name, one-sentence rule, role, clock, "?"),
    /// the group with call / text buttons, the private and public facts rendered from the typed LineFact data, and the
    /// controls from LineControlView (Number keypad, Choice, Player, Multi, Items). Submit sends SubmitTask with C = the
    /// TaskField mask of the fields being set; locked / waiting / blocked / submitted states are shown; sabotage options
    /// need an explicit confirmation that states the rule. The 5-step tutorial docks next to it.
    /// </summary>
    public sealed class DeskTaskApp : DeskAppView
    {
        sealed class Ctl
        {
            public LineControlView C;
            public VisualElement Card;
            public Label[] Cells;
            public Label Hint, Note;
            public readonly List<(VisualElement tile, LineOption opt)> Tiles = new List<(VisualElement, LineOption)>();
            public Action<int> PickUpdate;
        }

        public DeskTaskApp(DeskUi ui) : base(ui, "dk-app-task") { }
        public override DeskApp Id => DeskApp.Task;
        public override string Icon => "task_app";

        // staged values per TaskField (index = (int)field: 1 Target, 2 A, 4 B)
        readonly string[] _entry = new string[8];
        readonly int[] _sel = new int[8];
        readonly int[] _mask = new int[8];
        int _taskKey = int.MinValue, _staticSig = int.MinValue, _dynSig = int.MinValue, _srvSig = int.MinValue;
        int _openedNumber = -1;
        readonly List<Ctl> _ctls = new List<Ctl>();
        readonly List<(int id, VisualElement tick, Button call, Button text)> _members = new List<(int, VisualElement, Button, Button)>();
        VisualElement _head, _groupBox, _infoBox, _pubBox, _ctrlBox, _submitBox, _confirmBox, _bodyRow;
        ScrollView _leftScroll, _rightScroll;
        DeskTutorial _tut;
        Label _timer, _status;
        VisualElement _timerBox;
        Button _primary, _help;
        int _lastSec = -1;
        bool _confirming;
        int _mockSel = int.MinValue;

        /// <summary>Coach-arrow anchors (null when not built).</summary>
        public VisualElement CallTarget { get; private set; }
        public VisualElement SubmitTarget => _primary;
        public bool TutorialOpen => _tut != null && _tut.IsOpen;

        public override string Subtitle(LineView v)
        {
            if (v.Task == null || v.RoundIndex < 0) return Dk.PhaseName(v.Phase);
            return Dk.F("desk.clock.round", v.RoundIndex + 1, v.TodayTasks.Count, Dk.TaskName(v.Task.Kind));
        }

        public override int Badge(LineView v)
        {
            return v.Phase == LinePhase.RoundPlay && v.Task != null && !v.Task.Submitted ? -1 : 0;
        }

        // ------------------------------------------------------------------ tutorial

        public void AutoTutorial(LineView v)
        {
            if (v.Task == null) return;
            if (Ui.State.TutAutoNumber == v.Task.Number) return;
            Ui.State.TutAutoNumber = v.Task.Number;
            Refresh(v, true);
            OpenTutorial("auto");
        }

        public void OpenTutorial(string action)
        {
            var v = Dk.View;
            if (v == null || v.Task == null || _tut == null) return;
            Ui.State.TutSeen.Add((int)v.Task.Kind);
            _tut.Open(v.Task.Kind, action, ApplyHighlight, null);
        }

        internal void MockTutorialStep(int step) { if (_tut != null) { if (!_tut.IsOpen) OpenTutorial("reopen"); _tut.MockStep(step); } }

        public void CloseTutorial() { if (_tut != null) _tut.Close(false); }

        void ApplyHighlight(int step)
        {
            var targets = new[] { _head, _infoBox, _groupBox, _ctrlBox, _submitBox };
            for (int i = 0; i < targets.Length; i++) if (targets[i] != null) targets[i].EnableInClassList("dk-hl", i == step);
            if (step < 0 || step >= targets.Length || targets[step] == null) return;
            var el = targets[step];
            var sv = step == 1 || step == 2 ? _leftScroll : step >= 3 ? _rightScroll : null;
            if (sv != null) el.schedule.Execute(() => { try { sv.ScrollTo(el); } catch (Exception) { } }).StartingIn(60);
        }

        public override bool Back()
        {
            if (_confirming) { _confirming = false; _dynSig = int.MinValue; UpdateDynamic(Dk.View); return true; }
            if (_tut != null && _tut.IsOpen) { _tut.Close(false); return true; }
            return false;
        }

        public override void OnShown()
        {
            var v = Dk.View;
            if (v != null) ReportOpened(v);
        }

        void ReportOpened(LineView v)
        {
            if (v.Task == null || _openedNumber == v.Task.Number) return;
            _openedNumber = v.Task.Number;
            TelemetryHooks.ChoiceUiOpened(v.Task.Kind, Math.Max(0, Dk.Now - v.PhaseStartTime));
        }

        // ------------------------------------------------------------------ refresh

        public override void Refresh(LineView v, bool force)
        {
            int ss = StaticSig(v);
            if (force || ss != _staticSig)
            {
                int key = v.Task != null ? v.Task.Number * 100 + (int)v.Task.Kind : -1 - v.Day;
                if (key != _taskKey)
                {
                    _taskKey = key;
                    _confirming = false;
                    if (_tut != null) _tut.Dismiss();
                    for (int i = 0; i < 8; i++) { _entry[i] = ""; _sel[i] = -1; _mask[i] = 0; }
                    _srvSig = int.MinValue;
                }
                _staticSig = ss;
                Build(v);
                _dynSig = int.MinValue;
            }
            if (v.Task != null)
            {
                ReportOpened(v);
                int srv = ServerSig(v.Task);
                if (srv != _srvSig) { _srvSig = srv; Resync(v.Task); _dynSig = int.MinValue; }
            }
            int ds = DynSig(v);
            if (ds != _dynSig) { _dynSig = ds; UpdateDynamic(v); }
        }

        static int StaticSig(LineView v)
        {
            var t = v.Task;
            if (t == null) return (int)v.Phase * 7 + v.Day * 131 + v.TodayTasks.Count;
            int h = (int)t.Kind;
            h = h * 31 + t.Number;
            h = h * 31 + t.Role;
            h = h * 31 + t.Group.Count * 17 + (t.Group.Count > 0 ? t.Group[0] : 0);
            h = h * 31 + t.Facts.Count;
            for (int i = 0; i < t.Facts.Count; i++) h = h * 31 + t.Facts[i].N1 * 5 + t.Facts[i].N2 + t.Facts[i].P;
            h = h * 31 + t.PublicFacts.Count;
            for (int i = 0; i < t.PublicFacts.Count; i++) h = h * 31 + t.PublicFacts[i].N1 * 5 + t.PublicFacts[i].N2 + t.PublicFacts[i].P;
            h = h * 31 + t.Controls.Count;
            for (int i = 0; i < t.Controls.Count; i++) h = h * 31 + t.Controls[i].Options.Count * 3 + (t.Controls[i].Enabled ? 1 : 0);
            h = h * 31 + t.Items.Count;
            for (int i = 0; i < t.Items.Count; i++) h = h * 31 + t.Items[i];
            h = h * 31 + v.Players.Count;
            return h;
        }

        static int ServerSig(LineTaskView t)
        {
            return ((t.MyA * 31 + t.MyB) * 31 + t.MyTarget) * 31 + (int)t.SubmittedFields;
        }

        static int DynSig(LineView v)
        {
            var t = v.Task;
            int h = (int)v.Phase;
            if (t != null)
            {
                h = h * 31 + (t.Submitted ? 1 : 0);
                h = h * 31 + (int)t.SubmittedFields;
                for (int i = 0; i < t.Controls.Count; i++) h = h * 31 + t.Controls[i].WaitingFor + (t.Controls[i].Enabled ? 1 : 0);
            }
            int mask = 0;
            for (int i = 0; i < v.Players.Count; i++)
            {
                if (v.Players[i].Submitted) mask |= 1 << v.Players[i].Id;
                if (v.Players[i].Connected) mask |= 1 << (v.Players[i].Id + 8);
            }
            h = h * 31 + mask;
            h = h * 31 + (Dk.ComputerBlocked(v) ? 1 : 0) + (Dk.PhoneDead(v) ? 2 : 0);
            h = h * 31 + (int)v.Mine.Phone.Status * 10 + v.Mine.Phone.Other;
            return h;
        }

        /// <summary>Server values of the fields I already submitted become the staged values (after my own submit or a reconnect).</summary>
        void Resync(LineTaskView t)
        {
            foreach (var c in t.Controls)
            {
                int f = (int)c.Field;
                if (f == 0 || (t.SubmittedFields & c.Field) == 0) continue;
                switch (c.Field)
                {
                    case TaskField.A: SetStaged(c, t.MyA); break;
                    case TaskField.B: SetStaged(c, t.MyB); break;
                    case TaskField.Target: SetStaged(c, t.MyTarget); break;
                }
            }
        }

        void SetStaged(LineControlView c, int value)
        {
            int f = (int)c.Field;
            switch (c.Kind)
            {
                case ControlKind.Number: _entry[f] = value >= 0 ? value.ToString() : ""; break;
                case ControlKind.Multi: _mask[f] = Mathf.Max(0, value); break;
                default: _sel[f] = value; break;
            }
        }

        public override void Tick(LineView v)
        {
            if (_timer == null) return;
            int s = Mathf.CeilToInt((float)(v.PhaseEndTime - Dk.Now));
            if (s < 0) s = 0;
            if (s != _lastSec)
            {
                _lastSec = s;
                _timer.SetText(ClockInt(s));
                bool urgent = v.Phase == LinePhase.RoundPlay && s <= 15;
                _timerBox.EnableInClassList("dk-task-clock-urgent", urgent);
            }
        }

        public void OnSubmitted(int actor) { _dynSig = int.MinValue; }

        public override bool OnResult(LineActionResult r, LineAction req)
        {
            if (req.Verb != LineVerb.SubmitTask && req.Verb != LineVerb.GiveItem) return false;
            if (r.Accepted)
            {
                if (req.Verb == LineVerb.SubmitTask) { Dk.Play("stamp", null, 0.6f); if (_primary != null) UiMotion.Pulse(_primary, 0.1f, 0.3f); }
                return false;
            }
            if (_status != null)
            {
                _status.SetText(Dk.T("desk.fail." + r.Failure));
                _status.AddToClassList("dk-task-status-bad");
                if (_primary != null) Dk.Shake(_primary, 6f, 0.3f);
            }
            return true;
        }

        // ------------------------------------------------------------------ build

        void Build(LineView v)
        {
            Root.Clear();
            _ctls.Clear();
            _members.Clear();
            CallTarget = null;
            _primary = null; _timer = null; _status = null; _confirmBox = null; _timerBox = null;
            _head = _groupBox = _infoBox = _pubBox = _ctrlBox = _submitBox = null;
            var t = v.Task;
            if (t == null) { BuildEmpty(v); return; }
            var kind = t.Kind;

            // header
            _head = Box("dk-task-head").AddTo(Root);
            var ic = Box("dk-task-icon").NoPick().AddTo(_head);
            ic.style.backgroundColor = Dk.TaskColor(kind);
            Dk.Icon(Dk.TaskIcon(kind), "dk-task-icon-img").AddTo(ic);
            var hc = Box("dk-task-head-col").NoPick().AddTo(_head);
            var nameRow = Box("dk-task-namerow").NoPick().AddTo(hc);
            Title(Dk.TaskName(kind), "dk-task-name").NoPick().AddTo(nameRow);
            string role = Dk.RoleText(kind, t.Role);
            if (role.Length > 0) Tag(Dk.F("desk.task.role", role), "tag-green").AddTo(nameRow);
            Text(Dk.TaskRule(kind), "dk-task-rule").NoPick().AddTo(hc);
            _timerBox = Box("dk-task-clock").NoPick().AddTo(_head);
            Dk.Icon("timer", "dk-task-clock-icon").AddTo(_timerBox);
            _timer = MemoBold("", "dk-task-clock-text").NoPick().AddTo(_timerBox);
            _help = Btn("?", () => { if (_tut.IsOpen) _tut.Close(false); else OpenTutorial("reopen"); }, "btn-secondary", "dk-help").AddTo(_head);
            _lastSec = -1;

            // body: left (group + facts), right (controls + submit), tutorial dock
            _bodyRow = Box("dk-task-body").AddTo(Root);
            _leftScroll = new ScrollView(ScrollViewMode.Vertical).Cls("dk-task-left").AddTo(_bodyRow);
            _rightScroll = new ScrollView(ScrollViewMode.Vertical).Cls("dk-task-right").AddTo(_bodyRow);
            _tut = new DeskTutorial(_bodyRow);
            var left = _leftScroll.contentContainer;
            var right = _rightScroll.contentContainer;

            BuildGroup(left, v, t);
            BuildFacts(left, v, t);
            _ctrlBox = Box("dk-ctrlbox").AddTo(right);
            BuildControls(_ctrlBox, v, t);
            _submitBox = Box("dk-submitbox").AddTo(right);
            _status = Text("", "dk-task-status").NoPick().AddTo(_submitBox);
            _confirmBox = Box("dk-confirm").AddTo(_submitBox);
            _confirmBox.Show(false);
            var row = Box("dk-submit-row").AddTo(_submitBox);
            _primary = Btn(Dk.T("desk.task.send"), OnPrimary, "btn-primary", "dk-primary", "dk-task-send").AddTo(row);
            Dk.Key("Enter", "dk-key-s").AddTo(row);
            Dk.NoRich(Root);
        }

        void BuildEmpty(LineView v)
        {
            var box = Box("dk-task-empty").AddTo(Root);
            Dk.Icon("task_app", "dk-task-empty-icon").AddTo(box);
            Title(Dk.T("desk.task.none"), "dk-task-empty-title").NoPick().AddTo(box);
            Text(Dk.T("desk.task.none_sub"), "dk-task-empty-sub").NoPick().AddTo(box);
            var row = Box("dk-task-empty-row").NoPick().AddTo(box);
            for (int i = 0; i < v.TodayTasks.Count; i++)
            {
                var c = Box("dk-task-empty-card").NoPick().AddTo(row);
                Dk.TaskCard(v.TodayTasks[i], "dk-taskcard-s").AddTo(c);
                Bold(Dk.F("desk.round_n", i + 1) + " · " + Dk.TaskName(v.TodayTasks[i]), "dk-task-empty-name").NoPick().AddTo(c);
                Text(Dk.TaskRule(v.TodayTasks[i]), "dk-task-empty-rule").NoPick().AddTo(c);
            }
            _tut = null;
        }

        static string GroupCaption(LineTaskView t, int i)
        {
            string id = LineTasks.Id(t.Kind);
            string key = t.Kind == TaskKind.ReportReview ? "task." + id + ".group." + i : t.Kind == TaskKind.ArchiveVisitor ? "task." + id + ".group." + t.Role : null;
            if (key != null && Loc.Has(key)) return Dk.T(key);
            return Dk.T(t.Family == TaskFamily.Pair ? "desk.task.partner" : "desk.task.colleague");
        }

        void BuildGroup(VisualElement parent, LineView v, LineTaskView t)
        {
            _groupBox = Box("dk-groupbox").AddTo(parent);
            Dk.Heading(_groupBox, Dk.T(t.Family == TaskFamily.Pair ? "desk.task.group_pair" : t.Family == TaskFamily.Role ? "desk.task.group_role" : "desk.task.group_all"), Dk.T("desk.task.group_hint"));
            if (t.Group.Count == 0) { Text(Dk.T("desk.task.group_none"), "dk-empty").AddTo(_groupBox); return; }
            for (int i = 0; i < t.Group.Count; i++)
            {
                int id = t.Group[i];
                var p = Dk.Player(id);
                var row = Box("dk-gm").AddTo(_groupBox);
                Dk.Badge(id, "badge-s").AddTo(row);
                var col = Box("dk-gm-col").NoPick().AddTo(row);
                Bold(p != null ? p.Name : Dk.Name(id), "dk-gm-name").Plain().NoPick().AddTo(col);
                Text(GroupCaption(t, i), "dk-gm-cap").NoPick().AddTo(col);
                Box("spacer").NoPick().AddTo(row);
                var tick = Tag(Dk.T("desk.task.sent"), "tag-green").AddTo(row);
                var call = IconBtn("call", Dk.T("desk.task.call"), () => Ui.Call(id), "btn-secondary", "btn-small", "dk-gm-btn").AddTo(row);
                var text = IconBtn("text", Dk.T("desk.task.text"), () => Ui.OpenMessages(id), "btn-secondary", "btn-small", "dk-gm-btn").AddTo(row);
                _members.Add((id, tick, call, text));
                if (CallTarget == null) CallTarget = call;
            }
        }

        void BuildFacts(VisualElement parent, LineView v, LineTaskView t)
        {
            _infoBox = Box("dk-infobox").AddTo(parent);
            Dk.Heading(_infoBox, Dk.T("desk.task.info"), Dk.T("desk.task.info_hint"));
            var paper = Box("dk-paper").AddTo(_infoBox);
            if (t.Facts.Count == 0) Text(Dk.T("desk.task.info_none"), "dk-empty").AddTo(paper);
            for (int i = 0; i < t.Facts.Count; i++) FactRow(paper, t.Kind, t.Facts[i]);
            if (t.Items.Count > 0)
            {
                var held = Box("dk-fact").NoPick().AddTo(paper);
                Box("dk-bullet").NoPick().AddTo(held);
                string items = "";
                for (int i = 0; i < t.Items.Count; i++) items += (i > 0 ? ", " : "") + Dk.ItemName(t.Items[i]);
                Text(Dk.F("desk.task.holding", items), "dk-fact-text").NoPick().AddTo(held);
            }
            if (t.PublicFacts.Count > 0)
            {
                _pubBox = Box("dk-pubbox").AddTo(parent);
                Dk.Heading(_pubBox, Dk.T("desk.task.public"), Dk.T("desk.task.public_hint"));
                var board = Box("dk-paper", "dk-paper-pub").AddTo(_pubBox);
                for (int i = 0; i < t.PublicFacts.Count; i++) FactRow(board, t.Kind, t.PublicFacts[i]);
            }
        }

        static void FactRow(VisualElement parent, TaskKind kind, LineFact f)
        {
            var row = Box("dk-fact").NoPick().AddTo(parent);
            Box("dk-bullet").NoPick().AddTo(row);
            Text(Dk.FactText(kind, f), "dk-fact-text").NoPick().AddTo(row);
            if (f.Scope == 1) Tag(Dk.T("desk.task.partner_sees"), "tag-grey").AddTo(row);
        }

        // ------------------------------------------------------------------ controls

        void BuildControls(VisualElement parent, LineView v, LineTaskView t)
        {
            Dk.Heading(parent, Dk.T("desk.task.controls"), Dk.T("desk.task.controls_hint"));
            for (int i = 0; i < t.Controls.Count; i++)
            {
                var c = t.Controls[i];
                var ctl = new Ctl { C = c };
                ctl.Card = Box("dk-ctl").AddTo(parent);
                var head = Box("dk-ctl-head").NoPick().AddTo(ctl.Card);
                Bold(Dk.ControlLabel(t.Kind, c), "dk-ctl-label").NoPick().AddTo(head);
                Box("spacer").NoPick().AddTo(head);
                if (c.Sabotage) Tag(Dk.T("desk.task.sabotage_tag"), "tag-red").AddTo(head);
                if (c.Locks) Tag(Dk.T("desk.task.locks_tag"), "tag-grey").AddTo(head);
                Tag(Dk.T(c.Required ? "desk.task.required" : "desk.task.optional"), c.Required ? "tag-green" : "tag-grey").AddTo(head);
                switch (c.Kind)
                {
                    case ControlKind.Number: BuildNumber(ctl); break;
                    case ControlKind.Choice: BuildChoice(ctl, t, false); break;
                    case ControlKind.Multi: BuildChoice(ctl, t, true); break;
                    case ControlKind.Player: BuildPlayer(ctl, v); break;
                    case ControlKind.Items: BuildItems(ctl, v, t); break;
                }
                ctl.Note = Text("", "dk-ctl-note").NoPick().AddTo(ctl.Card);
                _ctls.Add(ctl);
            }
        }

        void BuildNumber(Ctl ctl)
        {
            var c = ctl.C;
            int f = (int)c.Field;
            var disp = Box("dk-numdisp").NoPick().AddTo(ctl.Card);
            int cells = c.Digits > 0 ? c.Digits : Mathf.Max(1, c.Max.ToString().Length);
            ctl.Cells = new Label[cells];
            for (int i = 0; i < cells; i++) ctl.Cells[i] = MemoBold("", "dk-cell").NoPick().AddTo(disp);
            ctl.Hint = Text(c.Digits > 0 ? Dk.F("desk.task.digits", c.Digits) : Dk.F("desk.task.range", c.Min, c.Max), "dk-ctl-hint").NoPick().AddTo(ctl.Card);
            var pad = Box("dk-keypad").AddTo(ctl.Card);
            int[] order = { 7, 8, 9, 4, 5, 6, 1, 2, 3, -1, 0, -2 };
            for (int i = 0; i < order.Length; i++)
            {
                int d = order[i];
                string label = d >= 0 ? d.ToString() : d == -1 ? Dk.T("desk.task.key_del") : Dk.T("desk.task.key_clear");
                Btn(label, () => PressDigit(f, c, d), "btn-secondary", "dk-keypad-key", d < 0 ? "dk-keypad-fn" : "").AddTo(pad);
            }
        }

        void PressDigit(int f, LineControlView c, int d)
        {
            var v = Dk.View;
            if (v == null || !Interactive(v) || Locked(v.Task, c) || c.WaitingFor >= 0) { Dk.Play("deny"); return; }
            string e = _entry[f] ?? "";
            int max = c.Digits > 0 ? c.Digits : Mathf.Max(1, c.Max.ToString().Length);
            if (d == -1) { if (e.Length > 0) e = e.Substring(0, e.Length - 1); }
            else if (d == -2) e = "";
            else if (e.Length < max) e += d.ToString();
            _entry[f] = e;
            _confirming = false;
            _dynSig = int.MinValue;
            UpdateDynamic(v);
        }

        /// <summary>Keyboard digits and backspace (-1) go to the first number control.</summary>
        public override void Digit(int d)
        {
            for (int i = 0; i < _ctls.Count; i++)
                if (_ctls[i].C.Kind == ControlKind.Number && _ctls[i].C.Enabled) { PressDigit((int)_ctls[i].C.Field, _ctls[i].C, d); return; }
        }

        void BuildChoice(Ctl ctl, LineTaskView t, bool multi)
        {
            var c = ctl.C;
            int f = (int)c.Field;
            var list = Box("dk-opts").AddTo(ctl.Card);
            if (multi) Text(Dk.F("desk.task.max_picks", c.MaxPicks), "dk-ctl-hint").NoPick().AddTo(ctl.Card);
            for (int i = 0; i < c.Options.Count; i++)
            {
                var o = c.Options[i];
                var tile = Dk.Tile(() => PickOption(ctl, o, multi), "dk-opt").AddTo(list);
                if (Dk.IsSabotage(c, o)) tile.AddToClassList("dk-opt-sab");
                Box("dk-opt-mark").NoPick().AddTo(tile);
                Bold(Dk.OptionText(t.Kind, o), "dk-opt-text").Plain().NoPick().AddTo(tile);
                if (Dk.IsSabotage(c, o)) Tag(Dk.T("desk.task.sabotage_tag"), "tag-red").AddTo(tile);
                ctl.Tiles.Add((tile, o));
            }
            list.AddToClassList(c.Options.Count > 5 ? "dk-opts-grid" : "dk-opts-col");
        }

        void PickOption(Ctl ctl, LineOption o, bool multi)
        {
            var v = Dk.View;
            var c = ctl.C;
            if (v == null || !Interactive(v) || Locked(v.Task, c) || c.WaitingFor >= 0 || !o.Enabled) { Dk.Play("deny"); return; }
            int f = (int)c.Field;
            if (multi)
            {
                int bit = 1 << o.Value;
                if ((_mask[f] & bit) != 0) _mask[f] &= ~bit;
                else if (Pop(_mask[f]) < Mathf.Max(1, c.MaxPicks)) _mask[f] |= bit;
                else { Dk.Shake(ctl.Card, 6f, 0.3f); Dk.Play("deny"); }
            }
            else _sel[f] = _sel[f] == o.Value && !c.Required ? -1 : o.Value;
            _confirming = false;
            _dynSig = int.MinValue;
            UpdateDynamic(v);
        }

        static int Pop(int m) { int n = 0; while (m != 0) { n += m & 1; m >>= 1; } return n; }

        void BuildPlayer(Ctl ctl, LineView v)
        {
            var c = ctl.C;
            int f = (int)c.Field;
            ctl.PickUpdate = Dk.Picker(ctl.Card, v, id =>
            {
                var vv = Dk.View;
                if (vv == null || !Interactive(vv) || Locked(vv.Task, c) || c.WaitingFor >= 0) { Dk.Play("deny"); return; }
                _sel[f] = id; _confirming = false; _dynSig = int.MinValue; UpdateDynamic(vv);
            }, c.AllowSelf, "badge-m");
            if (c.AllowNone)
            {
                var none = Dk.Tile(() => { var vv = Dk.View; if (vv == null || !Interactive(vv)) return; _sel[f] = LineIds.Nobody; _dynSig = int.MinValue; UpdateDynamic(vv); }, "dk-opt", "dk-opt-none").AddTo(ctl.Card);
                Box("dk-opt-mark").NoPick().AddTo(none);
                Bold(Dk.T("desk.task.none_pick"), "dk-opt-text").NoPick().AddTo(none);
                ctl.Tiles.Add((none, new LineOption { Key = "none", Value = LineIds.Nobody }));
            }
            if (c.AllowSelf) Text(Dk.T("desk.task.self_ok"), "dk-ctl-hint").NoPick().AddTo(ctl.Card);
        }

        void BuildItems(Ctl ctl, LineView v, LineTaskView t)
        {
            Text(Dk.T("desk.task.items_hint"), "dk-ctl-hint").NoPick().AddTo(ctl.Card);
            if (t.Items.Count == 0) { Text(Dk.T("desk.task.items_none"), "dk-empty").AddTo(ctl.Card); return; }
            for (int i = 0; i < t.Items.Count; i++)
            {
                int index = i;
                var row = Box("dk-item").AddTo(ctl.Card);
                Dk.Icon("task_swap", "dk-item-icon").NoPick().AddTo(row);
                Bold(Dk.ItemName(t.Items[i]), "dk-item-name").NoPick().AddTo(row);
                Box("spacer").NoPick().AddTo(row);
                Text(Dk.T("desk.task.give_to"), "dk-ctl-hint").NoPick().AddTo(row);
                Dk.ChipPicker(row, v, id =>
                {
                    var vv = Dk.View;
                    if (vv == null || !Interactive(vv)) { Dk.Play("deny"); return; }
                    Dk.Send(LineVerb.GiveItem, id, index);
                    Dk.Play("pickup", "click");
                });
            }
        }

        // ------------------------------------------------------------------ state

        static bool Interactive(LineView v) { return v.Phase == LinePhase.RoundPlay && v.Task != null && !Dk.ComputerBlocked(v); }
        static bool Locked(LineTaskView t, LineControlView c) { return t != null && c.Locks && (t.SubmittedFields & c.Field) != 0; }

        /// <summary>Staged value valid for sending? (Number range/digits, option picked, picks within the limit.)</summary>
        bool Valid(Ctl ctl, out int value)
        {
            var c = ctl.C;
            int f = (int)c.Field;
            value = -1;
            switch (c.Kind)
            {
                case ControlKind.Number:
                {
                    string e = _entry[f] ?? "";
                    int n;
                    if (e.Length == 0 || !int.TryParse(e, out n)) return false;
                    if (c.Digits > 0 && e.Length != c.Digits) return false;
                    if (c.Digits == 0 && (n < c.Min || n > c.Max)) return false;
                    value = n;
                    return true;
                }
                case ControlKind.Choice: value = _sel[f]; return value >= 0;
                case ControlKind.Player: value = _sel[f]; return value != -1;
                case ControlKind.Multi: value = _mask[f]; return value != 0 && Pop(value) <= Mathf.Max(1, c.MaxPicks);
            }
            return false;
        }

        int ServerValue(LineTaskView t, Ctl ctl)
        {
            switch (ctl.C.Field)
            {
                case TaskField.A: return t.MyA;
                case TaskField.B: return t.MyB;
                case TaskField.Target: return t.MyTarget;
            }
            return -1;
        }

        /// <summary>Collects the fields to send now; returns the mask (0 = nothing to send).</summary>
        TaskField Collect(LineView v, out int target, out int a, out int b, out bool sabotage, List<string> sabNames)
        {
            var t = v.Task;
            TaskField mask = TaskField.None;
            target = LineIds.None; a = -1; b = -1; sabotage = false;
            for (int i = 0; i < _ctls.Count; i++)
            {
                var ctl = _ctls[i];
                var c = ctl.C;
                if (!c.Enabled || c.Field == TaskField.None || c.Kind == ControlKind.Items) continue;
                if (Locked(t, c) || c.WaitingFor >= 0) continue;
                int val;
                if (!Valid(ctl, out val)) continue;
                bool submitted = (t.SubmittedFields & c.Field) != 0;
                if (submitted && ServerValue(t, ctl) == val) continue; // unchanged
                mask |= c.Field;
                switch (c.Field)
                {
                    case TaskField.Target: target = val; break;
                    case TaskField.A: a = val; break;
                    case TaskField.B: b = val; break;
                }
                if (c.Kind == ControlKind.Choice)
                    for (int k = 0; k < c.Options.Count; k++)
                        if (c.Options[k].Value == val && Dk.IsSabotage(c, c.Options[k])) { sabotage = true; sabNames?.Add(Dk.OptionText(t.Kind, c.Options[k])); }
            }
            return mask;
        }

        void OnPrimary()
        {
            var v = Dk.View;
            if (v == null || !Interactive(v)) { Dk.Play("deny"); return; }
            if (_confirming) return;
            var names = new List<string>();
            int target, a, b; bool sab;
            var mask = Collect(v, out target, out a, out b, out sab, names);
            if (mask == TaskField.None) { Dk.Play("deny"); return; }
            if (sab) { _confirming = true; _confirmNames = names; _dynSig = int.MinValue; UpdateDynamic(v); return; }
            DoSubmit(v, mask, target, a, b);
        }

        List<string> _confirmNames;

        void DoSubmit(LineView v, TaskField mask, int target, int a, int b)
        {
            _confirming = false;
            Dk.Send(LineVerb.SubmitTask, target, a, b, (int)mask);
            if (_status != null) _status.RemoveFromClassList("dk-task-status-bad");
            _dynSig = int.MinValue;
        }

        void ConfirmSabotage()
        {
            var v = Dk.View;
            if (v == null || !Interactive(v)) { _confirming = false; return; }
            int target, a, b; bool sab;
            var mask = Collect(v, out target, out a, out b, out sab, null);
            if (mask == TaskField.None) { _confirming = false; _dynSig = int.MinValue; UpdateDynamic(v); return; }
            Dk.Play("stamp");
            DoSubmit(v, mask, target, a, b);
        }

        /// <summary>UI mock: stages a value without clicking (control index 0, option value or number).</summary>
        internal void MockSelect(int control, int value)
        {
            var v = Dk.View;
            if (v == null) return;
            for (int i = 0; i < _ctls.Count; i++)
            {
                if (control >= 0 && i != control) continue;
                var c = _ctls[i].C;
                int f = (int)c.Field;
                if (c.Kind == ControlKind.Choice || c.Kind == ControlKind.Player) { _sel[f] = value; if (control >= 0) break; }
                else if (c.Kind == ControlKind.Multi) { _mask[f] = value; if (control >= 0) break; }
                else if (c.Kind == ControlKind.Number) { _entry[f] = value.ToString(); if (control >= 0) break; }
            }
            _dynSig = int.MinValue;
            UpdateDynamic(v);
        }

        internal void MockConfirm()
        {
            var v = Dk.View;
            if (v == null) return;
            OnPrimary();
        }

        void UpdateDynamic(LineView v)
        {
            var t = v != null ? v.Task : null;
            if (t == null || _primary == null) return;
            bool play = v.Phase == LinePhase.RoundPlay;
            bool blocked = Dk.ComputerBlocked(v);
            bool phoneDead = Dk.PhoneDead(v);
            bool callIdle = v.Mine.Phone.Status == CallStatus.Idle && !phoneDead && v.Features.CallsOpen;

            // group rows
            for (int i = 0; i < _members.Count; i++)
            {
                var m = _members[i];
                var p = Dk.Player(m.id);
                m.tick.Show(p != null && p.Submitted);
                m.call.SetEnabled(callIdle && p != null && p.Connected);
            }

            // controls
            bool anyWaiting = false, anyLocked = false;
            for (int i = 0; i < _ctls.Count; i++)
            {
                var ctl = _ctls[i];
                var c = ctl.C;
                int f = (int)c.Field;
                bool locked = Locked(t, c);
                bool waiting = c.WaitingFor >= 0;
                anyWaiting |= waiting; anyLocked |= locked;
                bool usable = play && !blocked && c.Enabled && !locked && !waiting;
                ctl.Card.EnableInClassList("dk-ctl-off", !usable);
                ctl.Card.EnableInClassList("dk-ctl-locked", locked);
                switch (c.Kind)
                {
                    case ControlKind.Number:
                    {
                        string e = _entry[f] ?? "";
                        for (int k = 0; k < ctl.Cells.Length; k++)
                        {
                            ctl.Cells[k].SetText(k < e.Length ? e.Substring(k, 1) : "");
                            ctl.Cells[k].EnableInClassList("dk-cell-on", k == e.Length && usable);
                        }
                        int val; bool ok = Valid(ctl, out val);
                        ctl.Hint.EnableInClassList("dk-ctl-hint-bad", e.Length > 0 && !ok && (c.Digits == 0 || e.Length == c.Digits));
                        break;
                    }
                    case ControlKind.Choice:
                    case ControlKind.Multi:
                        for (int k = 0; k < ctl.Tiles.Count; k++)
                        {
                            var o = ctl.Tiles[k].opt;
                            bool on = c.Kind == ControlKind.Multi ? (_mask[f] & (1 << o.Value)) != 0 : _sel[f] == o.Value;
                            ctl.Tiles[k].tile.EnableInClassList("dk-opt-on", on);
                            ctl.Tiles[k].tile.SetEnabled(usable && o.Enabled);
                        }
                        break;
                    case ControlKind.Player:
                        ctl.PickUpdate?.Invoke(_sel[f]);
                        for (int k = 0; k < ctl.Tiles.Count; k++)
                        {
                            ctl.Tiles[k].tile.EnableInClassList("dk-opt-on", _sel[f] == LineIds.Nobody);
                            ctl.Tiles[k].tile.SetEnabled(usable);
                        }
                        break;
                    case ControlKind.Items:
                        ctl.Card.SetEnabled(play && !blocked);
                        break;
                }
                string note = "";
                if (locked) note = Dk.T("desk.task.locked_note");
                else if (waiting) note = Dk.F("desk.task.waiting", Dk.Name(c.WaitingFor));
                else if (!c.Enabled) note = Dk.T("desk.task.disabled_note");
                ctl.Note.SetText(note);
                ctl.Note.Show(note.Length > 0);
            }

            // submit area
            int target, a, b; bool sab;
            var mask = Collect(v, out target, out a, out b, out sab, null);
            bool submitted = t.Submitted;
            string status;
            if (blocked) status = Dk.T("desk.task.blocked");
            else if (v.Phase == LinePhase.RoundIntro) status = Dk.T("desk.task.not_started");
            else if (v.Phase != LinePhase.RoundPlay) status = Dk.T("desk.task.closed");
            else if (mask != TaskField.None) status = submitted ? Dk.T("desk.task.changed") : Dk.T("desk.task.ready");
            else if (submitted) status = anyLocked ? Dk.T("desk.task.submitted_locked") : Dk.T("desk.task.submitted");
            else if (anyWaiting) status = Dk.T("desk.task.wait_first");
            else status = Dk.T("desk.task.pick_first");
            if (_status != null && !_status.ClassListContains("dk-task-status-bad")) _status.SetText(status);
            else if (_status != null && mask != TaskField.None) { _status.RemoveFromClassList("dk-task-status-bad"); _status.SetText(status); }
            _submitBox.EnableInClassList("dk-submit-done", submitted && mask == TaskField.None);
            _primary.SetText(Dk.T(submitted ? "desk.task.update" : "desk.task.send"));
            _primary.SetEnabled(play && !blocked && mask != TaskField.None && !_confirming);
            _primary.EnableInClassList("btn-danger", sab);
            _primary.EnableInClassList("btn-primary", !sab);

            // sabotage confirmation
            _confirmBox.Clear();
            _confirmBox.Show(_confirming);
            if (_confirming)
            {
                Title(Dk.T("desk.sab.title"), "dk-confirm-title").NoPick().AddTo(_confirmBox);
                if (_confirmNames != null) for (int i = 0; i < _confirmNames.Count; i++) Bold(_confirmNames[i], "dk-confirm-opt").Plain().NoPick().AddTo(_confirmBox);
                var cfg = Dk.Config;
                Text(Dk.F("desk.sab.rule", cfg.CareerSabotagePenalty, cfg.ExposedSabotageBlame), "dk-confirm-rule").NoPick().AddTo(_confirmBox);
                bool lockHit = false;
                for (int i = 0; i < _ctls.Count; i++) if (_ctls[i].C.Locks && _ctls[i].C.Kind == ControlKind.Choice) lockHit = true;
                if (lockHit) Text(Dk.T("desk.sab.locks"), "dk-confirm-rule").NoPick().AddTo(_confirmBox);
                var row = Box("dk-confirm-row").AddTo(_confirmBox);
                Btn(Dk.T("desk.sab.back"), () => { _confirming = false; _dynSig = int.MinValue; UpdateDynamic(Dk.View); }, "btn-secondary").AddTo(row);
                Btn(Dk.T("desk.sab.do"), ConfirmSabotage, "btn-danger", "dk-confirm-do").AddTo(row);
                UiMotion.PopIn(_confirmBox, 0.2f, 0f, 0.92f);
            }
            Dk.NoRich(_submitBox);
        }
    }
}
