using System.Linq;
using BirimizKovulacak.Core;
using UnityEngine;
using EventType = BirimizKovulacak.Core.EventType;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.UiKit;

namespace BirimizKovulacak.Game
{
    /// <summary>HR meeting: dossier, accusations, note presentation, defenses, ballot and the verdict explanation.</summary>
    public sealed class MeetingScreen : UiKit.Screen
    {
        readonly Label _phase, _timer, _instruction;
        readonly VisualElement _dossier, _side;
        int _lastVersion = -1;
        MatchPhase _lastPhase;

        public MeetingScreen(VisualElement parent) : base(parent)
        {
            var top = Box("meeting-top").AddTo(Root);
            var pb = Box("phase-box").AddTo(top);
            _phase = Bold("", "phase-name").AddTo(pb);
            _timer = Title("", "timer").AddTo(pb);
            _instruction = Bold("", "light").AddTo(pb);
            var body = Box("meeting-body").AddTo(Root);
            var d = Box("card", "dossier").AddTo(body);
            Title(Loc.T("meeting.dossier"), "h2").AddTo(d);
            _dossier = new ScrollView().Cls("grow").AddTo(d).contentContainer;
            var s = Box("card", "side").AddTo(body);
            _side = new ScrollView().Cls("grow").AddTo(s).contentContainer;
        }

        public override void Tick()
        {
            if (!Visible) return;
            var cs = ClientState.I;
            var v = cs.View;
            if (v == null) return;
            double rem = v.PhaseEndTime - cs.Now;
            _timer.text = Clock(rem);
            _phase.text = Loc.Phase(v.Phase);
            if (v.StateVersion == _lastVersion && v.Phase == _lastPhase) return;
            _lastVersion = v.StateVersion;
            _lastPhase = v.Phase;
            _instruction.text = Instruction(v);
            BuildDossier(v);
            BuildSide(v);
        }

        static string Instruction(PlayerView v)
        {
            switch (v.Phase)
            {
                case MatchPhase.MeetingDiscussion: return Loc.T("meeting.instr.discussion");
                case MatchPhase.MeetingDefense1:
                case MatchPhase.MeetingDefense2: return Loc.F("meeting.instr.defense", Loc.PlayerName(v, v.Meeting.Speaker));
                case MatchPhase.MeetingVote: return Loc.T("meeting.instr.vote");
                case MatchPhase.MeetingVerdict: return Loc.T("meeting.instr.verdict");
                default: return "";
            }
        }

        void BuildDossier(PlayerView v)
        {
            _dossier.Clear();
            bool informantScan = v.Me.Role == Role.Informant && v.Phase == MatchPhase.MeetingDiscussion;
            if (v.Evidence.Count == 0) Text(Loc.T("phone.no_evidence"), "muted").AddTo(_dossier);
            for (int i = v.Evidence.Count - 1; i >= 0; i--) PhonePanel.EvidenceRow(_dossier, v.Evidence[i], v, informantScan);
            Bold(Loc.T("phone.observations"), "h3").AddTo(_dossier);
            foreach (var o in v.Observations.Where(o => o.Kind != ObservationKind.SuspiciousItemAtDesk).Reverse())
            {
                var r = Box("list-row").AddTo(_dossier);
                Chip(o.Subject).AddTo(r);
                Text(Describe.Observation(o, v), "list-row-text").AddTo(r);
            }
            Bold(Loc.T("meeting.log"), "h3").AddTo(_dossier);
            foreach (var l in v.Log.Where(l => l.Kind == EventType.WorkstationLocked || l.Kind == EventType.PhoneMissed || l.Kind == EventType.TaskCompleted).Reverse().Take(14))
            {
                ToastKindIgnored(l, v, out string s);
                if (s != null) Text(Loc.Time(l.Time, v) + " — " + s, "list-row").AddTo(_dossier);
            }
        }

        static void ToastKindIgnored(LogEntry l, PlayerView v, out string s)
        {
            var e = new GameEvent { Type = l.Kind, Time = l.Time, Actor = l.Actor, TargetId = l.TargetId, IntArg = l.IntArg, StrArg = l.StrArg };
            Hud.ToastKind k;
            s = Describe.Event(e, v, out k);
        }

        void BuildSide(PlayerView v)
        {
            _side.Clear();
            var me = v.Me;
            int mySlot = ClientState.I.MySlot;
            var m = v.Meeting;

            if (v.Phase == MatchPhase.MeetingVerdict && m.Verdict != null)
            {
                BuildVerdict(v, m.Verdict);
                return;
            }

            Title(Loc.T("meeting.people"), "h2").AddTo(_side);
            foreach (var p in v.Players)
            {
                var c = Box("cand").AddTo(_side);
                if (m.Speaker == p.Id) c.AddToClassList("cand-speaking");
                var row = Box("row").AddTo(c);
                Chip(p.Id, true).AddTo(row);
                Bold(p.Name + (p.Id == mySlot ? " " + Loc.T("hud.you") : ""), "h3", "grow").AddTo(row);
                Pips(p.SuspicionLevel).AddTo(row);
                Text(Loc.F("meeting.stats", p.Performance.ToString("0"), p.AccusationsReceived) + (m.Shortlist.Contains(p.Id) ? " · " + Loc.T("meeting.shortlisted") : ""), "muted").AddTo(c);
                var acc = m.Accusations.Where(a => a.Accused == p.Id).Select(a => Loc.PlayerName(v, a.Accuser)).ToList();
                if (acc.Count > 0) Text(Loc.F("meeting.accused_by", string.Join(", ", acc)), "warn", "small").AddTo(c);

                if (v.Phase == MatchPhase.MeetingDiscussion && p.Id != mySlot && !me.HasAccused)
                    Btn(Loc.T("meeting.accuse"), () => ClientState.I.Send(ActionVerb.Accuse, "", p.Id), "btn-small", "btn-danger").AddTo(c);
                if (v.Phase == MatchPhase.MeetingVote && m.Shortlist.Contains(p.Id) && p.Id != mySlot)
                {
                    var b = Btn(me.Ballot == p.Id ? Loc.T("meeting.voted_for") : Loc.T("meeting.vote_fire"), () => ClientState.I.Send(ActionVerb.CastBallot, "", p.Id), "btn-small", me.Ballot == p.Id ? "btn-selected" : "btn-primary").AddTo(c);
                    b.SetEnabled(!me.HasVoted);
                }
            }
            if (me.HasAccused) Text(Loc.F("meeting.you_accused", Loc.PlayerName(v, me.AccusedPlayer)), "accent").AddTo(_side);
            if (v.Phase == MatchPhase.MeetingVote)
            {
                var b = Btn(Loc.T("meeting.abstain"), () => ClientState.I.Send(ActionVerb.CastBallot, "", PlayerIds.None), "btn-small").AddTo(_side);
                b.SetEnabled(!me.HasVoted);
                Text(Loc.F("meeting.ballots", m.BallotsCast, v.Players.Count(p => p.Connected)), "muted").AddTo(_side);
            }

            bool canPresent = v.Phase == MatchPhase.MeetingDiscussion || v.Phase == MatchPhase.MeetingDefense1 || v.Phase == MatchPhase.MeetingDefense2;
            if (canPresent)
            {
                Title(Loc.T("meeting.my_notes"), "h3").AddTo(_side);
                Text(Loc.T("meeting.present_help"), "muted", "small", "label-wrap").AddTo(_side);
                int presented = me.Notes.Count(n => n.Presented);
                foreach (var n in me.Notes.Where(n => !n.Presented))
                {
                    var r = Box("list-row").AddTo(_side);
                    Text(Describe.Note(n, v), "list-row-text").AddTo(r);
                    var b = Btn(Loc.T("meeting.present"), () => ClientState.I.Send(ActionVerb.PresentNote, "", n.Id), "btn-small").AddTo(r);
                    b.SetEnabled(presented < 2);
                }
                if (me.Notes.Count == 0) Text(Loc.T("phone.no_notes"), "muted").AddTo(_side);
            }
            Text(Loc.T("meeting.discord_hint"), "muted", "small", "label-wrap").AddTo(_side);
        }

        void BuildVerdict(PlayerView v, VerdictView vd)
        {
            Title(Loc.T("meeting.verdict_title"), "h2").AddTo(_side);
            var c = Box("player-row").AddTo(_side);
            Chip(vd.FiredPlayer, true).AddTo(c);
            Title(Loc.PlayerName(v, vd.FiredPlayer).ToUpperInvariant(), "verdict-name").AddTo(_side);
            Text(Loc.F("meeting.formula", (vd.VoteWeight * 100).ToString("0"), (vd.RiskWeight * 100).ToString("0"), (vd.PerformanceWeight * 100).ToString("0")), "muted", "small", "label-wrap").AddTo(_side);
            var table = Box("score-table").AddTo(_side);
            var head = Box("row").AddTo(table);
            foreach (var h in new[] { "meeting.col_player", "meeting.col_votes", "meeting.col_risk", "meeting.col_perf", "meeting.col_score" })
                Bold(Loc.T(h), "score-cell").AddTo(head);
            foreach (var cand in vd.Candidates)
            {
                var r = Box("row").AddTo(table);
                Bold(Loc.PlayerName(v, cand.Player), "score-cell").AddTo(r);
                Text(cand.Votes + " (" + (cand.VoteShare * 100).ToString("0") + "%)", "score-cell").AddTo(r);
                Text(cand.ObservedRisk.ToString("0.#") + " → " + cand.RiskNormalized.ToString("0.00"), "score-cell").AddTo(r);
                Text(cand.Performance.ToString("0") + " → " + cand.PerformanceDeficit.ToString("0.00"), "score-cell").AddTo(r);
                Bold(cand.Score.ToString("0.000"), "score-cell").AddTo(r);
            }
            if (vd.TieBreakUsed != TieBreak.None) Text(Loc.T("meeting.tiebreak." + vd.TieBreakUsed), "accent").AddTo(_side);
            if (vd.KeyEvidence.Count > 0 || vd.KeyObservations.Count > 0)
            {
                Bold(Loc.T("meeting.key_reasons"), "h3").AddTo(_side);
                foreach (int id in vd.KeyEvidence)
                {
                    var e = v.Evidence.FirstOrDefault(x => x.Id == id);
                    if (e != null) Text("• " + Describe.Evidence(e, v), "small", "label-wrap").AddTo(_side);
                }
                foreach (int id in vd.KeyObservations)
                {
                    var o = v.Observations.FirstOrDefault(x => x.Id == id);
                    if (o != null) Text("• " + Describe.Observation(o, v), "small", "label-wrap").AddTo(_side);
                }
            }
        }
    }

    /// <summary>Firing, full reveal (roles, objectives, true misconduct, who really did what), recap and rematch.</summary>
    public sealed class ResultsScreen : UiKit.Screen
    {
        readonly VisualElement _content;
        readonly Label _timer;
        int _builtFor = -1;
        MatchPhase _builtPhase;
        bool _played;

        public ResultsScreen(VisualElement parent) : base(parent)
        {
            var card = Box("card", "results-modal").AddTo(Root);
            var head = Box("row").AddTo(card);
            Title(Loc.T("results.title"), "h1", "grow").AddTo(head);
            _timer = Bold("", "muted").AddTo(head);
            _content = new ScrollView().Cls("grow").AddTo(card).contentContainer;
        }

        protected override void OnShow() { _builtFor = -1; _played = false; }

        public override void Tick()
        {
            if (!Visible) return;
            var cs = ClientState.I;
            var v = cs.View;
            if (v == null) return;
            int key = v.MatchId * 1000 + (v.Me.RematchVoted ? 1 : 0) + (v.Result != null ? 2 : 0) + v.Log.Count(l => l.Kind == EventType.RematchVote) * 10;
            _timer.text = v.Phase == MatchPhase.Results ? Loc.F("results.rematch_hint", Clock(v.PhaseEndTime - cs.Now)) : "";
            if (key == _builtFor && v.Phase == _builtPhase) return;
            _builtFor = key;
            _builtPhase = v.Phase;
            _content.Clear();
            if (v.Phase == MatchPhase.Abandoned)
            {
                Title(Loc.T("results.abandoned"), "big-result", "warn").AddTo(_content);
                Text(Loc.T("abandon." + v.AbandonReason), "h3").AddTo(_content);
                Text(Loc.T("results.abandoned_note"), "muted").AddTo(_content);
                Btn(Loc.T("pause.leave"), () => ConnectionManager.I.Leave(), "btn-danger").AddTo(_content);
                return;
            }
            var r = v.Result;
            if (r == null) { Text(Loc.T("results.waiting"), "muted").AddTo(_content); return; }
            if (!_played)
            {
                _played = true;
                Sfx.Play("fired");
            }

            Title(Loc.F("results.fired", Loc.PlayerName(v, r.FiredPlayer).ToUpperInvariant()), "big-result", "warn").AddTo(_content);
            Bold(r.CompanySucceeded ? Loc.F("results.company_ok", r.QuotaPoints.ToString("0"), r.QuotaTarget.ToString("0")) : Loc.F("results.company_fail", r.QuotaPoints.ToString("0"), r.QuotaTarget.ToString("0")), "h2", r.CompanySucceeded ? "good" : "warn").AddTo(_content);
            Bold(Loc.F("results.informant_was", Loc.PlayerName(v, r.InformantPlayer)) + " " + Loc.T(r.InformantWon ? "results.informant_won" : "results.informant_lost"), "h3").AddTo(_content);

            Title(Loc.T("results.players"), "h2").AddTo(_content);
            float maxMis = Mathf.Max(1f, r.Players.Count == 0 ? 1f : r.Players.Max(p => p.TrueMisconduct));
            foreach (var p in r.Players.OrderBy(p => p.MisconductRank))
            {
                var row = Box("outcome-row", p.Won ? "outcome-win" : "outcome-lose").AddTo(_content);
                Chip(p.Player, true).AddTo(row);
                var col = Box("col", "grow").AddTo(row);
                Bold(p.Name + " — " + Loc.T(p.Role == Role.Informant ? "role.informant" : "role.employee") + (p.Fired ? " · " + Loc.T("results.fired_tag") : ""), "h3").AddTo(col);
                string obj = p.Role == Role.Informant ? Loc.ObjectiveName(ObjectiveType.InformantGoal) : Loc.ObjectiveName(p.ObjectiveType) + (p.ObjectiveTarget >= 0 ? " (" + Loc.PlayerName(v, p.ObjectiveTarget) + ")" : "");
                Text(obj + " — " + Loc.T(p.ObjectiveCompleted ? "results.obj_done" : "results.obj_not_done") + " · " + Loc.T(p.ExplanationKey), "small", "label-wrap").AddTo(col);
                Text(Loc.F("results.stats", p.FinalPerformance.ToString("0.#"), p.TasksCompleted, p.SabotageCount, p.TrueMisconduct.ToString("0.#")) + (p.FalseAccusationPenalty > 0 ? " · " + Loc.F("results.false_accusation", p.FalseAccusationPenalty.ToString("0.#")) : ""), "small").AddTo(col);
                var bar = Box("mis-bar").AddTo(col);
                bar.style.width = Length.Percent(Mathf.Clamp01(p.TrueMisconduct / maxMis) * 60f + 1f);
                if (p.Titles.Count > 0) Text(string.Join(" · ", p.Titles.Select(Loc.T)), "accent").AddTo(col);
                Bold(Loc.T(p.Won ? "results.win" : "results.lose"), p.Won ? "good" : "warn").AddTo(row);
            }

            Title(Loc.T("results.truth"), "h2").AddTo(_content);
            if (r.Incidents.Count == 0) Text(Loc.T("results.no_incidents"), "muted").AddTo(_content);
            foreach (var i in r.Incidents) Text("• " + Loc.Time(i.Time, v) + " " + Describe.Incident(i, v), "small", "label-wrap").AddTo(_content);

            Title(Loc.T("results.recap"), "h2").AddTo(_content);
            foreach (var b in r.Recap) Text("• " + Describe.Recap(b, v), "small", "label-wrap").AddTo(_content);

            var row2 = Box("row").AddTo(_content);
            var rem = Btn(v.Me.RematchVoted ? Loc.T("results.rematch_voted") : Loc.T("results.rematch"), () => ClientState.I.Send(ActionVerb.RematchVote), "btn-primary").AddTo(row2);
            rem.SetEnabled(!v.Me.RematchVoted && v.Phase == MatchPhase.Results);
            int votes = v.Log.Count(l => l.Kind == EventType.RematchVote);
            Text(Loc.F("results.rematch_votes", votes, v.Players.Count(p => p.Connected)), "muted").AddTo(row2);
            if (ConnectionManager.I.HostController != null)
            {
                Btn(Loc.T("results.force_rematch"), () => ConnectionManager.I.HostController.StartRematch(), "btn-small").AddTo(row2);
                Btn(Loc.T("pause.return_lobby"), () => ConnectionManager.I.HostController.ReturnToLobby(), "btn-small").AddTo(row2);
            }
            Text(Loc.F("results.hash", r.ResultHash), "muted", "small").AddTo(_content);
        }
    }
}
