using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>Morning briefing: quota, desk, private role card and objective.</summary>
    public sealed class BriefingScreen : UiKit.Screen
    {
        readonly VisualElement _body;
        readonly Label _timer;
        bool _dismissed;
        int _builtFor = -1;
        public bool WantsCursor => Visible && !_dismissed;

        public BriefingScreen(VisualElement parent) : base(parent, "center")
        {
            var card = Box("card").AddTo(Root);
            card.style.width = 760;
            var head = Box("row").AddTo(card);
            Title(Loc.T("briefing.title"), "h1", "grow").AddTo(head);
            _timer = Title("", "h1", "accent").AddTo(head);
            _body = Box("col").AddTo(card);
            Btn(Loc.T("briefing.go"), () => { _dismissed = true; Root.Show(false); }, "btn-primary").AddTo(card);
        }

        protected override void OnShow() { _dismissed = false; Root.Show(true); }

        public override void Tick()
        {
            if (!Visible) return;
            var v = ClientState.I.View;
            if (v == null) return;
            _timer.text = Clock(v.PhaseEndTime - ClientState.I.Now);
            if (_builtFor == v.MatchId) return;
            _builtFor = v.MatchId;
            _dismissed = false;
            Root.Show(true);
            _body.Clear();
            var me = ClientState.I.Me;
            Text(Loc.F("briefing.quota", v.Quota.Target.ToString("0")), "h3").AddTo(_body);
            if (me != null) Text(Loc.F("briefing.desk", me.DeskIndex + 1, Loc.T("player.default." + me.DeskIndex)), "label-wrap").AddTo(_body);
            var lobby = ClientState.I.Lobby;
            string day = lobby != null && lobby.MatchRunning && lobby.StatusKey.StartsWith("day.") ? lobby.StatusKey : DayModifiers.Normal;
            var dayCard = Box("objective-card").AddTo(_body);
            Bold(Loc.F("briefing.day", Loc.T(day)), "h3").AddTo(dayCard);
            Text(Loc.T(day + ".desc") + " " + Loc.T("briefing.conditions"), "label-wrap").AddTo(dayCard);
            PhonePanel.BuildRoleCards(_body, v);
            Text(Loc.T("briefing.tips"), "small", "label-wrap").AddTo(_body);
        }
    }

    /// <summary>
    /// The private "phone" (Tab): objective, private notes, evidence board, and — only for the Informant —
    /// audit scans and the sealed report. Every player opens the same panel, so using it is no tell.
    /// </summary>
    public sealed class PhonePanel : UiKit.Screen
    {
        enum Tab { Objective, Notes, Evidence, Informant }
        Tab _tab = Tab.Objective;
        readonly VisualElement _tabs, _content;
        int _lastVersion = -1;
        Tab _lastTab;

        public PhonePanel(VisualElement parent) : base(parent, "dim")
        {
            var card = Box("card", "modal").AddTo(Root);
            var head = Box("row").AddTo(card);
            Title(Loc.T("phone.title"), "h1", "grow").AddTo(head);
            Btn(Loc.T("common.close"), Hide, "btn-small").AddTo(head);
            _tabs = Box("tabs").AddTo(card);
            var scroll = new ScrollView(ScrollViewMode.Vertical).Cls("grow").AddTo(card);
            _content = scroll.contentContainer;
        }

        public void Toggle() { SetVisible(!Visible); _lastVersion = -1; }
        public void Hide() { SetVisible(false); }

        public override void Tick()
        {
            if (!Visible) return;
            var v = ClientState.I.View;
            if (v == null) return;
            if (v.StateVersion == _lastVersion && _tab == _lastTab) return;
            _lastVersion = v.StateVersion;
            _lastTab = _tab;
            _tabs.Clear();
            AddTab(Tab.Objective, "phone.tab_objective");
            AddTab(Tab.Notes, "phone.tab_notes");
            AddTab(Tab.Evidence, "phone.tab_evidence");
            if (v.Me.Role == Role.Informant) AddTab(Tab.Informant, "phone.tab_informant");
            _content.Clear();
            switch (_tab)
            {
                case Tab.Objective: BuildRoleCards(_content, v); break;
                case Tab.Notes: BuildNotes(_content, v); break;
                case Tab.Evidence: BuildEvidence(_content, v, v.Me.Role == Role.Informant); break;
                case Tab.Informant: BuildInformant(_content, v); break;
            }
        }

        void AddTab(Tab t, string key)
        {
            var b = Btn(Loc.T(key), () => { _tab = t; _lastVersion = -1; }, "tab").AddTo(_tabs);
            b.RemoveFromClassList("btn");
            if (t == _tab) b.AddToClassList("tab-active");
        }

        public static void BuildRoleCards(VisualElement parent, PlayerView v)
        {
            var me = v.Me;
            var role = Box("role-card").AddTo(parent);
            if (me.Role == Role.Informant) role.AddToClassList("informant");
            Bold(Loc.T(me.Role == Role.Informant ? "role.informant" : "role.employee"), "h2", "light").AddTo(role);
            Text(Loc.T(me.Role == Role.Informant ? "role.informant.desc" : "role.employee.desc"), "light", "label-wrap").AddTo(role);

            var o = me.Objective;
            if (o == null) return;
            var card = Box("objective-card").AddTo(parent);
            Bold(Loc.ObjectiveName(o.Type), "h2").AddTo(card);
            string target = o.Target >= 0 ? Loc.PlayerName(v, o.Target) : "";
            Text(Loc.F("objective." + o.Type + ".desc", target, o.Required, o.Required2), "label-wrap").AddTo(card);
            string progress = Loc.F("objective.progress", o.Progress, o.Required);
            if (o.Required > 0) Text(progress + (o.Required2 > 0 ? " · " + Loc.F("objective.progress2", o.Progress2, o.Required2) : ""), "h3").AddTo(card);
            Text(Loc.T("objective.status." + o.Status), "accent").AddTo(card);
            foreach (var f in o.Flags) if (Loc.Has("objective.flag." + f)) Text(Loc.T("objective.flag." + f), "muted").AddTo(card);
            Text(Loc.T(me.Role == Role.Informant ? "objective.informant_gate" : "objective.gate"), "muted", "small", "label-wrap").AddTo(card);
        }

        static void BuildNotes(VisualElement parent, PlayerView v)
        {
            Text(Loc.T("phone.notes_help"), "muted", "label-wrap").AddTo(parent);
            if (v.Me.Notes.Count == 0) Text(Loc.T("phone.no_notes"), "muted").AddTo(parent);
            for (int i = v.Me.Notes.Count - 1; i >= 0; i--)
            {
                var n = v.Me.Notes[i];
                var r = Box("list-row").AddTo(parent);
                Text(Describe.Note(n, v), "list-row-text").AddTo(r);
                if (n.Presented) Text(Loc.T("phone.presented"), "badge").AddTo(r);
            }
        }

        public static void BuildEvidence(VisualElement parent, PlayerView v, bool informantTools)
        {
            Bold(Loc.T("phone.evidence_board"), "h3").AddTo(parent);
            if (v.Evidence.Count == 0) Text(Loc.T("phone.no_evidence"), "muted").AddTo(parent);
            for (int i = v.Evidence.Count - 1; i >= 0; i--)
                EvidenceRow(parent, v.Evidence[i], v, informantTools);
            Bold(Loc.T("phone.observations"), "h3").AddTo(parent);
            if (v.Observations.Count == 0) Text(Loc.T("phone.no_observations"), "muted").AddTo(parent);
            for (int i = v.Observations.Count - 1; i >= 0; i--)
            {
                var o = v.Observations[i];
                var r = Box("list-row").AddTo(parent);
                Chip(o.Subject).AddTo(r);
                Text(Describe.Observation(o, v), "list-row-text").AddTo(r);
                if (Mathf.Abs(o.Severity) > 0.01f) Text((o.Severity > 0 ? "+" : "") + o.Severity.ToString("0.#"), "badge").AddTo(r);
            }
        }

        public static void EvidenceRow(VisualElement parent, EvidenceView e, PlayerView v, bool informantTools)
        {
            var r = Box("list-row").AddTo(parent);
            int who = e.AttributedTo >= 0 ? e.AttributedTo : e.SubjectPlayer;
            if (who >= 0) Chip(who).AddTo(r);
            Text(Describe.Evidence(e, v), "list-row-text").AddTo(r);
            string st = Describe.Status(e);
            if (!string.IsNullOrEmpty(st))
            {
                var b = Text(st, "badge").AddTo(r);
                b.AddToClassList(e.Sealed ? "badge-seal" : e.Status == EvidenceStatus.Moved ? "badge-moved" : "badge-gone");
            }
            if (!informantTools || v.Me.Informant == null) return;
            bool scannable = e.Kind == EvidenceKind.SuspiciousItemFound || e.Kind == EvidenceKind.HiddenReportFound;
            bool sealable = scannable || e.Kind == EvidenceKind.WorkstationLockLog;
            bool office = LocalPlayerController.OfficePhase(v.Phase);
            if (scannable && (office || v.Phase == MatchPhase.MeetingDiscussion))
            {
                var b = Btn(Loc.F("informant.scan_btn", v.Me.Informant.ScansLeft), () => ClientState.I.Send(ActionVerb.AuditScanEvidence, "", e.Id), "btn-small").AddTo(r);
                b.SetEnabled(v.Me.Informant.ScansLeft > 0);
            }
            if (sealable && office && !e.Sealed && e.Status == EvidenceStatus.Active)
            {
                var b = Btn(Loc.F("informant.seal_btn", v.Me.Informant.SealsLeft), () => ClientState.I.Send(ActionVerb.SealEvidence, "", e.Id), "btn-small", "btn-danger").AddTo(r);
                b.SetEnabled(v.Me.Informant.SealsLeft > 0);
            }
        }

        static void BuildInformant(VisualElement parent, PlayerView v)
        {
            var inf = v.Me.Informant;
            if (inf == null) return;
            Bold(Loc.F("informant.charges", inf.ScansLeft, inf.SealsLeft), "h2").AddTo(parent);
            Text(Loc.T("informant.help"), "label-wrap", "muted").AddTo(parent);

            Bold(Loc.T("informant.nearby"), "h3").AddTo(parent);
            var avatar = PlayerAvatar.Local;
            int shown = 0;
            if (avatar != null && LocalPlayerController.OfficePhase(v.Phase))
            {
                float range = ClientState.I.Config.Informant.ItemScanRange;
                foreach (var it in v.Items)
                {
                    if (it.Kind != ItemKind.Suspicious && it.Kind != ItemKind.Report && it.Kind != ItemKind.Nameplate) continue;
                    Vector3 p;
                    if (!OfficeWorld.I.TryTargetPosition(it.Id, out p)) continue;
                    Vector3 d = p - avatar.transform.position; d.y = 0;
                    if (d.magnitude > range) continue;
                    var r = Box("list-row").AddTo(parent);
                    Text(Hud.ItemLabel(it, v) + " — " + (it.Location == ItemLocationKind.Slot ? Loc.Location(it.SlotId) : Loc.T("informant.on_floor")), "list-row-text").AddTo(r);
                    var b = Btn(Loc.F("informant.scan_btn", inf.ScansLeft), () => ClientState.I.Send(ActionVerb.AuditScanItem, it.Id), "btn-small").AddTo(r);
                    b.SetEnabled(inf.ScansLeft > 0);
                    shown++;
                }
            }
            if (shown == 0) Text(Loc.T("informant.nothing_nearby"), "muted").AddTo(parent);

            Bold(Loc.T("informant.results"), "h3").AddTo(parent);
            if (inf.Scans.Count == 0) Text(Loc.T("informant.no_scans"), "muted").AddTo(parent);
            foreach (var s in inf.Scans) Text(Describe.Scan(s, v), "list-row").AddTo(parent);
            if (inf.SealedEvidenceId >= 0) Text(Loc.F("informant.sealed", inf.SealedEvidenceId), "accent").AddTo(parent);

            Bold(Loc.T("phone.evidence_board"), "h3").AddTo(parent);
            for (int i = v.Evidence.Count - 1; i >= 0; i--) EvidenceRow(parent, v.Evidence[i], v, true);
        }
    }
}
