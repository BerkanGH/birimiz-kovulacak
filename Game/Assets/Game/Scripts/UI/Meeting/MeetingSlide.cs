using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;
using static BirimizKovulacak.Game.PlayBlocks;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 step 3a: the meeting's public information as a card over the room (top left) — what step 3b moves onto the
    /// projector: the dossier (failed tasks, whistles, investigations, leaks with their state), the vote and its
    /// progress, the verdict, the crisis result and the candidates, the defence order with the speaker's countdown, the
    /// desk reveal of a firing, and the results (winner, ranking, play again / leave, details in tabs; centred, with the
    /// cursor). Content comes from <see cref="PlayBlocks"/>; <see cref="Build"/> writes into any element, so 3b can
    /// render it into the projector canvas instead.
    /// </summary>
    public sealed class MeetingSlide
    {
        readonly VisualElement _card;
        readonly Label _kicker;
        readonly ScrollView _scroll;
        readonly PlaySection _body;
        readonly Func<string, string, float, bool> _toast;
        string _resultsTab = TabBallots;
        bool _dirty = true;

        const string TabFirings = "firings", TabBallots = "ballots", TabTransfers = "transfers", TabChoices = "choices", TabIncidents = "incidents";
        /// <summary>UI mocks: switch the results details to this tab.</summary>
        public static string MockResultsTab;

        public MeetingSlide(VisualElement parent, Func<string, string, float, bool> toast)
        {
            _toast = toast;
            _card = Box("mt-slide").AddTo(parent);
            var head = Box("mt-slide-head").NoPick().AddTo(_card);
            Box("mt-slide-lamp").NoPick().AddTo(head);
            _kicker = Bold("", "mt-slide-kicker").NoPick().AddTo(head);
            _scroll = new ScrollView(ScrollViewMode.Vertical).Cls("mt-slide-scroll");
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            // no scroll bar over the room (the whole file is in the folder's Dosya tab; the wheel still scrolls)
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _card.Add(_scroll);
            _body = new PlaySection(_scroll.contentContainer, "mt-slide-body");
            _card.Show(false);
        }

        public VisualElement Root => _card;
        /// <summary>Results and an abandoned match need the cursor (buttons).</summary>
        public bool WantsCursor { get; private set; }

        public void MarkDirty() { _dirty = true; }
        public void ResetWeek() { _resultsTab = TabBallots; _dirty = true; }

        public void Tick(LineClient lc, LineView v, double now, bool dirty, bool live, bool hidden)
        {
            if (MockResultsTab != null) { _resultsTab = MockResultsTab; MockResultsTab = null; _dirty = true; }
            bool results = v.Phase == LinePhase.Results || v.Phase == LinePhase.Abandoned;
            WantsCursor = results;
            _card.EnableInClassList("mt-slide-results", results);
            var bar = results ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
            if (_scroll.verticalScrollerVisibility != bar) _scroll.verticalScrollerVisibility = bar;
            bool show = !hidden && Has(v);
            if (show != _card.IsShown()) { _card.Show(show); if (show) UiMotion.FadeIn(_card, 0.2f); }
            if (!show) return;
            _kicker.SetText(Kicker(v));
            if (dirty || _dirty)
            {
                _dirty = false;
                _body.Update(p => Build(p, lc, v, now));
            }
            if (live) _body.TickLive(now);
        }

        static bool Has(LineView v) { return LineClient.IsMeetingPhase(v.Phase) || v.Phase == LinePhase.Results || v.Phase == LinePhase.Abandoned; }

        static string Kicker(LineView v)
        {
            if (v.Phase == LinePhase.Results) return Loc.Upper(T("meeting.slide.results"));
            if (v.Meeting != null && v.Meeting.Index < 2 && v.Phase >= LinePhase.MeetingDossier && v.Phase <= LinePhase.MeetingVerdict)
                return Loc.Upper(F("header.meeting", v.Meeting.Index + 1) + " · " + PlayText.Phase(v.Phase));
            return Loc.Upper(PlayText.Phase(v.Phase));
        }

        // ------------------------------------------------------------------ content

        public void Build(VisualElement p, LineClient lc, LineView v, double now)
        {
            switch (v.Phase)
            {
                case LinePhase.MeetingDossier:
                case LinePhase.MeetingDiscussion: Discussion(p, lc, v, now); break;
                case LinePhase.MeetingVote:
                case LinePhase.CrisisVote:
                case LinePhase.FinalVote: Vote(p, lc, v, now); break;
                case LinePhase.MeetingVerdict: MeetingVerdict(p, v); break;
                case LinePhase.CrisisVerdict: CrisisVerdict(p, lc, v); break;
                case LinePhase.FinalDiscussion: FinalDiscussion(p, lc, v, now); break;
                case LinePhase.FinalDefense: Defense(p, lc, v, now); break;
                case LinePhase.FinalVerdict: Verdict(p, lc, v); break;
                case LinePhase.Results: Results(p, lc, v); break;
                case LinePhase.Abandoned:
                    Big(p, T("abandoned.text"));
                    Btn(T("results.leave"), () => ConnectionManager.I?.Leave(), "pl-big").AddTo(p);
                    break;
            }
        }

        void Discussion(VisualElement p, LineClient lc, LineView v, double now)
        {
            var mt = v.Meeting;
            if (mt == null) { Big(p, PlayText.Hint(v.Phase)); return; }
            if (mt.FailedTasks.Count == 0) Big(p, T("focus.meeting_clean"));
            else
            {
                Big(p, T(mt.Index == 0 ? "focus.meeting1" : "focus.meeting2"));
                Line(p, F("meeting.failed", JoinTasks(mt.FailedTasks)), "pl-bad");
                if (mt.VoteableCount >= 0) Line(p, mt.VoteableCount > 0 ? F("meeting.voteable", mt.VoteableCount) : T("meeting.no_vote"));
                else Line(p, T("focus.meeting_vote_next"), "pl-dim");
            }
            if (mt.Roster.Count > 0) Line(p, F("meeting.roster", PlayText.Names(mt.Roster)));
            Dossier(p, v, now);
        }

        /// <summary>Dossier lines and the leaked recordings with their live state (projector content in 3b).</summary>
        static void Dossier(VisualElement p, LineView v, double now)
        {
            var mt = v.Meeting;
            if (mt == null) return;
            if (mt.Dossier.Count > 0)
            {
                Sub(p, T(mt.Index < 2 ? "meeting.dossier" : "meeting.final_dossier"));
                foreach (var e in mt.Dossier) Line(p, "• " + PlayText.Dossier(e), e.Kind == DossierKind.Investigation || e.Kind == DossierKind.Leak ? "pl-warn" : null);
            }
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
        }

        static void Progress(VisualElement p, double now)
        {
            // read the current view: other players voting must not rebuild the card
            Live(p, "progress", now, t =>
            {
                var cur = LineClient.I != null ? LineClient.I.View : null;
                int voted = 0, connected = 0;
                if (cur != null) foreach (var pl in cur.Players) { if (pl.Voted) voted++; if (pl.Connected && !pl.Fired) connected++; }
                return F("meeting.slide.progress", voted, connected);
            }, "pl-sub");
        }

        void Vote(VisualElement p, LineClient lc, LineView v, double now)
        {
            var c = lc.Config;
            var m = v.Mine;
            string text = v.Phase == LinePhase.MeetingVote ? F("focus.vote.MeetingVote", Math.Max(1, m.Ballots.Count), c.BlamePerVote)
                : v.Phase == LinePhase.CrisisVote ? F("focus.vote.CrisisVote", c.CrisisBlame)
                : v.Final != null && v.Final.Revote ? T("focus.vote.revote") : T("focus.vote.FinalVote");
            Big(p, text);
            Line(p, T(v.Phase == LinePhase.FinalVote ? "meeting.slide.secret_final" : "meeting.slide.secret"), "pl-dim");
            if (v.Phase == LinePhase.FinalVote && v.Final != null && v.Final.Options.Count > 0) Line(p, F("meeting.slide.options", PlayText.Names(v.Final.Options)), "pl-warn");
            Progress(p, now);
            if (v.Phase == LinePhase.MeetingVote) Dossier(p, v, now);
            if (v.Phase == LinePhase.FinalVote && v.Final != null) Firings(p, lc, v, false);
        }

        static void MeetingVerdict(VisualElement p, LineView v)
        {
            Big(p, T("hint.MeetingVerdict"));
            var mt = v.Meeting;
            if (mt == null || !mt.BlameGained.Exists(g => g.Amount > 0)) { Line(p, T("focus.no_blame"), "pl-dim"); return; }
            var row = Wrap(p);
            foreach (var g in mt.BlameGained)
                if (g.Amount > 0) Bold(Name(g.Player) + " " + F("meeting.blame_plus", g.Amount), "pl-chip", "pl-chip-warn").AddTo(row);
        }

        static void CrisisVerdict(VisualElement p, LineClient lc, LineView v)
        {
            Big(p, T("hint.CrisisVerdict"));
            var fv = v.Final;
            if (fv == null) return;
            if (fv.Crisis.Count > 0)
            {
                var row = Wrap(p);
                foreach (var g in fv.Crisis) Bold(Name(g.Player) + " " + F("meeting.blame_plus", g.Amount), "pl-chip", "pl-chip-warn").AddTo(row);
            }
            else Line(p, T("crisis.none"), "pl-dim");
            if (fv.Candidates.Count > 0) Line(p, F("focus.candidates", PlayText.Names(fv.DefenseOrder), fv.FiringsTotal), "pl-warn");
            Line(p, T("focus.sealed_note"));
            var meP = lc.Player(lc.MySlot);
            if (meP != null && meP.Candidate) Line(p, T("final.you_candidate"), "pl-bad");
        }

        static void FinalDiscussion(VisualElement p, LineClient lc, LineView v, double now)
        {
            var fv = v.Final;
            Big(p, F("focus.final_discussion", fv != null ? fv.Candidates.Count : 0, fv != null ? fv.FiringsTotal : 1));
            if (fv != null && fv.DefenseOrder.Count > 0) Line(p, F("focus.candidates", PlayText.Names(fv.DefenseOrder), fv.FiringsTotal), "pl-warn");
            var meP = lc.Player(lc.MySlot);
            if (meP != null && meP.Candidate) Line(p, T("final.you_candidate"), "pl-bad");
            if (fv != null && fv.Crisis.Count > 0)
            {
                Sub(p, T("crisis.result"));
                foreach (var g in fv.Crisis) Line(p, Name(g.Player) + ": " + F("meeting.blame_plus", g.Amount));
            }
            Dossier(p, v, now);
        }

        static void Defense(VisualElement p, LineClient lc, LineView v, double now)
        {
            var fv = v.Final;
            Big(p, T("hint.FinalDefense"));
            if (fv == null) return;
            int at = fv.DefenseOrder.IndexOf(fv.Defender);
            for (int i = 0; i < fv.DefenseOrder.Count; i++)
            {
                int id = fv.DefenseOrder[i];
                if (id == fv.Defender)
                {
                    string name = Name(id);
                    double end = fv.DefenderEndTime;
                    Live(p, "defender" + id + ":" + end, now, t => F("focus.defender", name, UiKit.Clock(Math.Max(0, end - t))), "pl-warn", "pl-sub");
                }
                else Line(p, F(at >= 0 && i < at ? "focus.defense_done" : "focus.defense_wait", Name(id)), "pl-dim");
            }
            if (fv.Defender == lc.MySlot) Line(p, T("focus.your_turn"), "pl-bad");
        }

        static void Verdict(VisualElement p, LineClient lc, LineView v)
        {
            var fv = v.Final;
            if (fv == null || fv.Firings.Count == 0) { Big(p, T("hint.FinalVerdict")); return; }
            BuildFiringCard(p, fv.Firings[fv.Firings.Count - 1], lc.MySlot, true);
            if (fv.Firings.Count < fv.FiringsTotal) Line(p, F("focus.next_firing", fv.Firings.Count + 1), "pl-dim");
            else Line(p, T("focus.next_results"), "pl-dim");
            if (fv.Firings.Count > 1) Firings(p, lc, v, true);
        }

        /// <summary>Earlier firings of this final (the latest one is the verdict card itself).</summary>
        static void Firings(VisualElement p, LineClient lc, LineView v, bool skipLast)
        {
            var fv = v.Final;
            int shown = skipLast ? fv.Firings.Count - 1 : fv.Firings.Count;
            if (shown <= 0) return;
            Sub(p, T("final.firings"));
            for (int i = 0; i < shown; i++) BuildFiringCard(p, fv.Firings[i], lc.MySlot, false);
        }

        void Results(VisualElement p, LineClient lc, LineView v)
        {
            var r = v.Results;
            int me = lc.MySlot;
            var buttons = Wrap(p);
            if (HostLineController.Current != null) Btn(T("results.continue"), () => HostLineController.Current?.ContinueAfterResults(), "pl-primary", "pl-big").AddTo(buttons);
            Btn(T("results.again"), () => { LineClient.I?.RequestRematch(true); TelemetryHooks.RematchRequested(); _toast?.Invoke(T("results.again_sent"), null, 5f); }, "pl-big").AddTo(buttons);
            Btn(T("results.leave"), () => ConnectionManager.I?.Leave(), "pl-big").AddTo(buttons);
            if (r == null) { Line(p, T("results.waiting"), "pl-dim"); return; }
            var players = SortedResults(r);
            int best = players.Count > 0 && players[0].Winner ? players[0].Prim : 0;
            Title(F(r.Winners.Count > 1 ? "results.winner_many" : "results.winner_one", PlayText.Names(r.Winners), best), "pl-win").AddTo(p);
            int rank = 0, prev = int.MinValue, myRank = 0;
            for (int i = 0; i < players.Count; i++)
            {
                var pr = players[i];
                int sortPrim = pr.Fired ? -1 : pr.Prim;
                if (sortPrim != prev) { rank = i + 1; prev = sortPrim; }
                if (pr.Player == me) myRank = rank;
                var row = Box("pl-rank", pr.Winner ? "pl-rank-win" : pr.Fired ? "pl-rank-fired" : null).AddTo(p);
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
                Line(p, mine.Fired ? T("results.me_fired") : mine.Winner ? F("results.me_won", mine.Prim) : F("results.me_rank", myRank, mine.Prim),
                    mine.Winner ? "pl-good" : mine.Fired ? "pl-bad" : null);
            Details(p, lc, r);
        }

        void Details(VisualElement p, LineClient lc, LineWeekResult r)
        {
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
    }
}
