using System;
using System.Collections.Generic;
using System.Text;
using BirimizKovulacak.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static BirimizKovulacak.Game.PlayKit;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 step 3b: the full reveal at the end of the match (docs/V5_DESIGN.md §5 "Final açılışı"), in the folder's Dosya tab:
    /// every balance and how it came about, who robbed whom ("Sarı benden 2 prim aldı ve yine de beni kovdu!"), every vote
    /// per round, the desk openings, the transfers, the task choices and the incidents. Only <see cref="LineWeekResult"/>
    /// (public at Results) and what this client saw itself during the match (its own task groups, <see cref="RevealMemory"/>).
    /// </summary>
    public static class MeetingReveal
    {
        public const string TabSummary = "summary", TabRobbed = "robbed", TabBallots = "ballots", TabFirings = "firings",
            TabTransfers = "transfers", TabChoices = "choices", TabIncidents = "incidents", TabPranks = "pranks";
        static readonly string[] Tabs = { TabSummary, TabRobbed, TabBallots, TabFirings, TabTransfers, TabChoices, TabIncidents, TabPranks };

        public static void Build(VisualElement p, LineClient lc, LineView v, string tab, Action<string> setTab)
        {
            var r = v.Results;
            if (r == null) { Line(p, T("results.waiting"), "pl-dim"); return; }
            int me = lc.MySlot;
            var players = PlayBlocks.SortedResults(r);
            int best = players.Count > 0 && players[0].Winner ? players[0].Prim : 0;
            Title(F(r.Winners.Count > 1 ? "results.winner_many" : "results.winner_one", PlayText.Names(r.Winners), best), "pl-win").AddTo(p);
            var mine = r.Players.Find(x => x.Player == me);
            if (mine != null)
            {
                int rank = 1;
                foreach (var o in players) if (!o.Fired && !mine.Fired && o.Prim > mine.Prim) rank++;
                Line(p, mine.Fired ? T("results.me_fired") : mine.Winner ? F("results.me_won", mine.Prim) : F("results.me_rank", rank, mine.Prim),
                    mine.Winner ? "pl-good" : mine.Fired ? "pl-bad" : null);
            }
            var robbed = Robberies(lc, r);
            var tabs = Wrap(p);
            foreach (var id in Tabs)
            {
                if (id == TabPranks && v.Pranks == null) continue;
                int n = id == TabPranks ? v.Pranks.Log.Count : id == TabSummary ? r.Players.Count : id == TabRobbed ? robbed.Count : id == TabBallots ? r.Ballots.Count : id == TabFirings ? r.Firings.Count
                    : id == TabTransfers ? r.Transfers.Count : id == TabChoices ? r.Choices.Count : r.Incidents.Count;
                string tid = id;
                Btn(F("reveal.tab." + id, n), () => setTab(tid), tab == id ? "pl-on" : null).AddTo(tabs);
            }
            var body = Box("pl-tabbody").AddTo(p);
            switch (tab)
            {
                case TabRobbed: BuildRobbed(body, lc, r, robbed); break;
                case TabBallots: PlayBlocks.BuildBallots(body, r); break;
                case TabFirings:
                    if (r.Firings.Count == 0) Line(body, T("common.none"), "pl-dim");
                    foreach (var f in r.Firings) PlayBlocks.BuildFiringCard(body, f, me, false);
                    break;
                case TabTransfers:
                    if (r.Transfers.Count == 0) Line(body, T("common.none"), "pl-dim");
                    foreach (var t in r.Transfers) Line(body, F("results.transfer", Name(t.From), Name(t.To), t.Amount, PlayText.Phase(t.Phase)), t.From == me || t.To == me ? "pl-private" : null);
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
                case TabIncidents:
                    if (r.Incidents.Count == 0) Line(body, T("common.none"), "pl-dim");
                    foreach (var e in r.Incidents) Line(body, "• " + PlayText.Dossier(e));
                    break;
                case TabPranks: BuildPranks(body, lc, v); break;
                default: BuildSummary(body, lc, r, players); break;
            }
        }

        // ------------------------------------------------------------------ balances

        /// <summary>Every colleague's prim, now public, and how it came about (start, tasks, whistles, investigation,
        /// transfers, desks).</summary>
        static void BuildSummary(VisualElement p, LineClient lc, LineWeekResult r, List<LineWeekPlayerResult> players)
        {
            int me = lc.MySlot;
            Sub(p, T("reveal.balances"));
            int start = lc.Config.StartPrim;
            foreach (var pr in players)
            {
                int task = 0, whistle = 0, inv = 0, transfer = 0, desk = 0, prank = 0;
                foreach (var c in r.PrimLog)
                {
                    if (c.Player != pr.Player) continue;
                    switch (c.Reason)
                    {
                        case PrimReason.Task: task += c.Delta; break;
                        case PrimReason.Whistled:
                        case PrimReason.WhistleReward: whistle += c.Delta; break;
                        case PrimReason.InvestigationCost:
                        case PrimReason.InvestigationRefund: inv += c.Delta; break;
                        case PrimReason.TransferIn:
                        case PrimReason.TransferOut: transfer += c.Delta; break;
                        case PrimReason.DeskShare:
                        case PrimReason.Fired: desk += c.Delta; break;
                        case PrimReason.Prank: prank += c.Delta; break;
                    }
                }
                var head = new StringBuilder();
                head.Append(Name(pr.Player));
                if (pr.Player == me) head.Append(" (").Append(T("players.me")).Append(')');
                head.Append(" · ").Append(F("header.prim", pr.Prim)).Append(" · ").Append(F("players.blame", pr.Blame));
                if (pr.Winner) head.Append(" · ").Append(T("results.winner"));
                if (pr.Fired) head.Append(" · ").Append(T("players.fired"));
                Bold(head.ToString(), "pl-text", pr.Winner ? "pl-good" : pr.Fired ? "pl-bad" : null).AddTo(p);
                var parts = new StringBuilder();
                parts.Append(F("reveal.start", start));
                Part(parts, "reveal.part.task", task);
                Part(parts, "reveal.part.whistle", whistle);
                Part(parts, "reveal.part.investigation", inv);
                Part(parts, "reveal.part.transfer", transfer);
                Part(parts, "reveal.part.prank", prank);
                Part(parts, "reveal.part.desk", desk);
                parts.Append("  →  ").Append(pr.Prim);
                Line(p, parts.ToString(), "pl-dim");
            }
        }

        /// <summary>v5 Yaramazlıklar: every prank of the match with its actor (phone rings revealed now), the prim burned per
        /// colleague and the tutanaks. The plane notes stay private (only your own are shown to you).</summary>
        static void BuildPranks(VisualElement p, LineClient lc, LineView v)
        {
            var pv = v.Pranks;
            if (pv == null || pv.Log.Count == 0) { Line(p, T("common.none"), "pl-dim"); return; }
            int me = lc.MySlot;
            var burned = new Dictionary<int, int>();
            int tutanak = 0, total = 0;
            foreach (var e in pv.Log)
            {
                if (e.Cost > 0) { burned.TryGetValue(e.Actor, out int b); burned[e.Actor] = b + e.Cost; total += e.Cost; }
                if (e.Tutanak) tutanak++;
            }
            Sub(p, F("reveal.pranks.head", pv.Log.Count, total, tutanak));
            if (burned.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var kv in burned)
                {
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append(F("reveal.pranks.burned", Name(kv.Key), kv.Value));
                }
                Line(p, sb.ToString(), "pl-dim");
            }
            int session = int.MinValue;
            foreach (var e in pv.Log)
            {
                if (e.Session != session) { session = e.Session; Sub(p, T("reveal.pranks.session." + Math.Max(0, Math.Min(4, session)))); }
                string line = "• " + MeetingPranks.LogLine(e, me);
                if (e.Kind == PrankKind.Plane && !string.IsNullOrEmpty(e.Note)) line += "  «" + MeetingPranks.NoteText(e.Note) + "»";
                Line(p, line, e.Tutanak ? "pl-bad" : e.Kind == PrankKind.PhoneRing ? "pl-warn" : e.Actor == me || e.Target == me ? "pl-private" : null);
            }
        }

        static void Part(StringBuilder sb, string key, int delta)
        {
            if (delta == 0) return;
            sb.Append(" · ").Append(F(key, PlayText.Signed(delta)));
        }

        // ------------------------------------------------------------------ who robbed whom

        public struct Robbery
        {
            public int TaskNumber, Taker, Victim, Lost;
            public TaskKind Kind;
            /// <summary>Petty Cash: taken from the cash box (no colleague lost prim).</summary>
            public bool Cash;
        }

        /// <summary>Client Bonus: a colleague who took it all while their partner(s) shared (the partners are known for my
        /// own group; for the others when the payouts leave only one possibility, else Victim = -1); Petty Cash: who took
        /// from the box.</summary>
        public static List<Robbery> Robberies(LineClient lc, LineWeekResult r)
        {
            var c = lc.Config;
            var list = new List<Robbery>();
            var byTask = new SortedDictionary<int, List<LineChoiceView>>();
            foreach (var ch in r.Choices)
            {
                List<LineChoiceView> l;
                if (!byTask.TryGetValue(ch.TaskNumber, out l)) byTask[ch.TaskNumber] = l = new List<LineChoiceView>();
                l.Add(ch);
            }
            foreach (var kv in byTask)
            {
                var choices = kv.Value;
                var kind = choices.Count > 0 ? choices[0].Task : TaskKind.None;
                if (kind == TaskKind.PettyCash)
                {
                    foreach (var ch in choices)
                        if (ch.Choice == LineChoice.Take) list.Add(new Robbery { TaskNumber = kv.Key, Kind = kind, Taker = ch.Player, Victim = -1, Lost = ch.Payout, Cash = true });
                    continue;
                }
                if (kind != TaskKind.ClientBonus) continue;
                // a sole taker gets the taker share, the partners who shared get the sharer share
                var takers = choices.FindAll(ch => ch.Choice == LineChoice.Take && (ch.Payout == c.BonusPairTaker || ch.Payout == c.BonusTrioTaker) && ch.Payout != c.BonusPairBothTake);
                var victims = choices.FindAll(ch => ch.Choice == LineChoice.Share && (ch.Payout == c.BonusPairSharer || ch.Payout == c.BonusTrioOthers) && ch.Payout < c.BonusPairShare);
                var mine = RevealMemory.GroupOf(kv.Key);
                if (mine != null)
                {
                    var t = takers.Find(ch => mine.Contains(ch.Player));
                    if (t != null)
                    {
                        foreach (var vct in victims.FindAll(ch => mine.Contains(ch.Player)))
                        {
                            list.Add(new Robbery { TaskNumber = kv.Key, Kind = kind, Taker = t.Player, Victim = vct.Player, Lost = Lost(c, mine.Count, vct.Payout) });
                            victims.Remove(vct);
                        }
                        takers.Remove(t);
                    }
                    else victims.RemoveAll(ch => mine.Contains(ch.Player));
                }
                if (takers.Count == 1)
                {
                    foreach (var vct in victims)
                        list.Add(new Robbery { TaskNumber = kv.Key, Kind = kind, Taker = takers[0].Player, Victim = vct.Player, Lost = Lost(c, victims.Count >= 2 ? 3 : 2, vct.Payout) });
                    if (victims.Count == 0) list.Add(new Robbery { TaskNumber = kv.Key, Kind = kind, Taker = takers[0].Player, Victim = -1, Lost = c.BonusPairShare });
                }
                else
                {
                    // more than one sole taker in other groups: who robbed whom is not known
                    foreach (var t in takers) list.Add(new Robbery { TaskNumber = kv.Key, Kind = kind, Taker = t.Player, Victim = -1, Lost = c.BonusPairShare });
                    foreach (var vct in victims) list.Add(new Robbery { TaskNumber = kv.Key, Kind = kind, Taker = -1, Victim = vct.Player, Lost = c.BonusPairShare - vct.Payout });
                }
            }
            return list;
        }

        static int Lost(LineConfig c, int groupSize, int payout) { return Math.Max(1, (groupSize >= 3 ? c.BonusTrioNone : c.BonusPairShare) - payout); }

        static void BuildRobbed(VisualElement p, LineClient lc, LineWeekResult r, List<Robbery> list)
        {
            int me = lc.MySlot;
            // my story first, in my own words ("Sarı benden 2 prim aldı ve yine de beni kovdu!")
            var story = new List<string>();
            var myFiring = r.Firings.Find(f => f.Player == me);
            foreach (var rb in list)
            {
                if (rb.Cash) continue;
                if (rb.Victim == me && rb.Taker >= 0)
                {
                    string who = Name(rb.Taker);
                    if (myFiring != null && myFiring.Recipients.Contains(rb.Taker)) story.Add(F("reveal.me.robbed_fired", who, rb.Lost));
                    else if (VotedAgainst(r, rb.Taker, me)) story.Add(F("reveal.me.robbed_voted", who, rb.Lost));
                    else
                    {
                        int paid = 0;
                        foreach (var t in r.Transfers) if (t.From == rb.Taker && t.To == me) paid += t.Amount;
                        story.Add(paid > 0 ? F("reveal.me.robbed_paid", who, rb.Lost, paid) : F("reveal.me.robbed", who, rb.Lost));
                    }
                }
                else if (rb.Taker == me && rb.Victim >= 0) story.Add(F("reveal.me.i_robbed", Name(rb.Victim), PlayText.Task(rb.Kind)));
            }
            if (myFiring != null)
                foreach (int id in myFiring.Recipients)
                {
                    bool counted = list.Exists(x => x.Victim == me && x.Taker == id);
                    if (!counted) story.Add(F("reveal.me.fired_by", Name(id), myFiring.Share));
                }
            if (story.Count > 0)
            {
                Sub(p, T("reveal.my_story"));
                foreach (var s in story) Bold("“" + s + "”", "pl-text", "pl-warn").AddTo(p);
            }
            Sub(p, T("reveal.robbed_title"));
            if (list.Count == 0) { Line(p, T("reveal.nobody_robbed"), "pl-good"); return; }
            foreach (var rb in list)
            {
                string task = F("reveal.task", rb.TaskNumber + 1, PlayText.Task(rb.Kind));
                string line = rb.Cash ? F("reveal.cash", Name(rb.Taker), task, rb.Lost)
                    : rb.Taker >= 0 && rb.Victim >= 0 ? F("reveal.pair", Name(rb.Taker), Name(rb.Victim), task)
                    : rb.Taker >= 0 ? F("reveal.taker", Name(rb.Taker), task)
                    : F("reveal.victim", Name(rb.Victim), task);
                Line(p, line, rb.Taker == me || rb.Victim == me ? "pl-private" : rb.Cash ? "pl-warn" : "pl-bad");
            }
            Line(p, T("reveal.robbed_note"), "pl-dim");
        }

        static bool VotedAgainst(LineWeekResult r, int voter, int target)
        {
            foreach (var b in r.Ballots) if (b.Voter == voter && b.Target == target && !b.SelfAssigned) return true;
            return false;
        }
    }

    /// <summary>
    /// What this client itself saw during the match and the results do not repeat: its own task groups (the partners of
    /// each pair / trio task). Used by <see cref="MeetingReveal"/> to say who robbed whom.
    /// </summary>
    public static class RevealMemory
    {
        static int _week = int.MinValue;
        static string _match;
        static readonly Dictionary<int, List<int>> _groups = new Dictionary<int, List<int>>();

        public static void Observe(LineView v)
        {
            if (v == null) return;
            if (v.WeekId != _week || v.MatchId != _match) { _week = v.WeekId; _match = v.MatchId; _groups.Clear(); }
            var t = v.Task;
            if (t == null || t.Group.Count == 0 || t.Kind == TaskKind.WeekendShift) return;
            if (!_groups.ContainsKey(t.Number)) _groups[t.Number] = new List<int>(t.Group);
        }

        /// <summary>My group in a task (me included), null when not seen.</summary>
        public static List<int> GroupOf(int taskNumber)
        {
            List<int> g;
            return _groups.TryGetValue(taskNumber, out g) ? g : null;
        }

        /// <summary>UI mocks: pretend this group was seen.</summary>
        public static void MockGroup(int weekId, string matchId, int taskNumber, params int[] ids)
        {
            if (weekId != _week || matchId != _match) { _week = weekId; _match = matchId; _groups.Clear(); }
            _groups[taskNumber] = new List<int>(ids);
        }
    }
}
