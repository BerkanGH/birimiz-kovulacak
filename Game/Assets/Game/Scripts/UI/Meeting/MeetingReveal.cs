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
    /// v5.2: the first tab is the story of the match (<see cref="LineStory"/>): "Manşetler", "Ödüller" and "Beyanlar" (every
    /// candidate's prim declaration against the truth); the results now carry the task groups, so who robbed whom is exact.
    /// v5.3: "Dosyalar": every file of the match (who held it on whom, opened when / shredded / expired) next to the prim
    /// that moved between the two; the story opens with the week's agenda.
    /// </summary>
    public static class MeetingReveal
    {
        public const string TabStory = "story", TabSummary = "summary", TabRobbed = "robbed", TabBallots = "ballots", TabFirings = "firings",
            TabTransfers = "transfers", TabChoices = "choices", TabIncidents = "incidents", TabPranks = "pranks", TabFiles = "files";
        static readonly string[] Tabs = { TabStory, TabSummary, TabRobbed, TabFiles, TabBallots, TabFirings, TabTransfers, TabChoices, TabIncidents, TabPranks };

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
            var story = StoryText.Of(v);
            var tabs = Wrap(p);
            foreach (var id in Tabs)
            {
                if (id == TabPranks && v.Pranks == null) continue;
                int n = id == TabFiles ? (r.Files != null ? r.Files.Count : 0) : id == TabPranks ? v.Pranks.Log.Count : id == TabStory ? (story != null ? story.Headlines.Count : 0) : id == TabSummary ? r.Players.Count : id == TabRobbed ? robbed.Count : id == TabBallots ? r.Ballots.Count : id == TabFirings ? r.Firings.Count
                    : id == TabTransfers ? r.Transfers.Count : id == TabChoices ? r.Choices.Count : r.Incidents.Count;
                string tid = id;
                Btn(F("reveal.tab." + id, n), () => setTab(tid), tab == id ? "pl-on" : null).AddTo(tabs);
            }
            var body = Box("pl-tabbody").AddTo(p);
            switch (tab)
            {
                case TabRobbed: BuildRobbed(body, lc, r, robbed); break;
                case TabFiles: BuildFiles(body, lc, r); break;
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
                case TabSummary: BuildSummary(body, lc, r, players); break;
                default: BuildStory(body, lc, v, story); break;
            }
        }

        // ------------------------------------------------------------------ files

        /// <summary>v5.3: every file of the match: its holder, its target, the task, what became of it (opened in which
        /// phase, shredded, void, expired), and under it the prim that moved between the two (the price of silence).</summary>
        static void BuildFiles(VisualElement p, LineClient lc, LineWeekResult r)
        {
            int me = lc.MySlot;
            Sub(p, T("reveal.files_title"));
            if (r.Files == null || r.Files.Count == 0) { Line(p, T("reveal.files_none"), "pl-good"); return; }
            foreach (var f in r.Files)
            {
                bool mine = f.Holder == me || f.Target == me;
                var row = Row(p);
                Line(row, F("reveal.file", Name(f.Holder), Name(f.Target), PlayText.Task(f.Task)), mine ? "pl-private" : null);
                Bold(PlayText.FileState(f), "pl-chip", f.State == LineFileState.Opened ? "pl-chip-bad" : f.State == LineFileState.Shredded ? "pl-chip-good" : null).AddTo(row);
                int paid = 0, back = 0;
                foreach (var t in r.Transfers)
                {
                    if (t.From == f.Target && t.To == f.Holder) paid += t.Amount;
                    else if (t.From == f.Holder && t.To == f.Target) back += t.Amount;
                }
                if (paid > 0 || back > 0) Line(p, F(back > 0 ? "reveal.file_money_back" : "reveal.file_money", Name(f.Target), Name(f.Holder), paid, back), "pl-dim", "pl-award-text");
            }
            Line(p, T("reveal.files_note"), "pl-dim");
        }

        // ------------------------------------------------------------------ the story: headlines, awards, declarations

        /// <summary>v5.2: what happened in a few lines ("Manşetler"), who earned which title ("Ödüller") and every
        /// candidate's prim declaration against the truth ("Beyanlar").</summary>
        static void BuildStory(VisualElement p, LineClient lc, LineView v, LineStory story)
        {
            var r = v.Results;
            int me = lc.MySlot;
            // v5.3: the week the match was played at
            string agenda = PlayText.Agenda(r.Stakes);
            if (agenda != null) Line(p, F("stakes.line", agenda), "pl-dim");
            Sub(p, T("reveal.headlines"));
            if (story == null || story.Headlines.Count == 0) Line(p, T("reveal.no_headlines"), "pl-dim");
            else
                foreach (var h in story.Headlines)
                    Bold("• " + StoryText.Headline(h, r), "pl-text", "pl-headline", h.Player == me || h.Other == me || h.Other2 == me ? "pl-private" : null).AddTo(p);
            Sub(p, T("reveal.awards"));
            if (story == null || story.Awards.Count == 0) Line(p, T("reveal.no_awards"), "pl-dim");
            else
                foreach (var a in story.Awards)
                {
                    var row = Row(p);
                    Bold(StoryText.AwardName(a.Kind), "pl-chip", "pl-award-chip").AddTo(row);
                    Bold(Name(a.Player) + (a.Player == me ? " (" + T("players.me") + ")" : ""), "pl-text", a.Player == me ? "pl-private" : null).AddTo(row);
                    Line(p, StoryText.AwardText(a), "pl-dim", "pl-award-text");
                }
            if (r.Declarations == null || r.Declarations.Count == 0) return;
            Sub(p, T("reveal.declarations"));
            foreach (var d in r.Declarations)
            {
                var row = Row(p);
                if (d.Amount < 0) { Line(row, F("reveal.declaration_none", Name(d.Player), d.Actual), "pl-dim"); continue; }
                Line(row, F("reveal.declaration", Name(d.Player), d.Amount, d.Actual), d.Player == me ? "pl-private" : null);
                Bold(T(d.Amount == d.Actual ? "declare.stamp.true" : "declare.stamp.lie"), "pl-chip", d.Amount == d.Actual ? "pl-chip-good" : "pl-chip-bad").AddTo(row);
            }
            Line(p, T("reveal.declarations_note"), "pl-dim");
        }

        // ------------------------------------------------------------------ balances

        /// <summary>Every colleague's prim, now public, and how it came about (start, tasks, transfers, pranks, desks).</summary>
        static void BuildSummary(VisualElement p, LineClient lc, LineWeekResult r, List<LineWeekPlayerResult> players)
        {
            int me = lc.MySlot;
            Sub(p, T("reveal.balances"));
            int start = lc.Config.StartPrim;
            foreach (var pr in players)
            {
                int task = 0, transfer = 0, desk = 0, prank = 0;
                foreach (var c in r.PrimLog)
                {
                    if (c.Player != pr.Player) continue;
                    switch (c.Reason)
                    {
                        case PrimReason.Task: task += c.Delta; break;
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
                Part(parts, "reveal.part.transfer", transfer);
                Part(parts, "reveal.part.prank", prank);
                Part(parts, "reveal.part.desk", desk);
                parts.Append("  →  ").Append(pr.Prim);
                Line(p, parts.ToString(), "pl-dim");
                // and the suç, by reason (a volunteer's cleaning is negative)
                var blame = new SortedDictionary<int, int>();
                foreach (var b in r.BlameLog)
                {
                    if (b.Player != pr.Player) continue;
                    blame.TryGetValue((int)b.Reason, out int x);
                    blame[(int)b.Reason] = x + b.Delta;
                }
                var bp = new StringBuilder();
                foreach (var kv in blame)
                {
                    if (kv.Value == 0) continue;
                    if (bp.Length > 0) bp.Append(" · ");
                    bp.Append(T("blame." + (BlameReason)kv.Key)).Append(' ').Append(PlayText.Signed(kv.Value));
                }
                if (bp.Length > 0) Line(p, F("reveal.blame_parts", bp.ToString()), "pl-dim");
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
                // v5.2: the results carry the groups: the only taker of a group robbed its sharers
                var groups = r.Groups != null ? r.Groups.FindAll(g => g != null && g.TaskNumber == kv.Key && g.Members != null) : null;
                if (groups != null && groups.Count > 0)
                {
                    foreach (var g in groups)
                    {
                        var took = choices.FindAll(ch => g.Members.Contains(ch.Player) && ch.Choice == LineChoice.Take);
                        if (took.Count != 1) continue;
                        foreach (var vct in choices.FindAll(ch => g.Members.Contains(ch.Player) && ch.Choice != LineChoice.Take))
                            list.Add(new Robbery { TaskNumber = kv.Key, Kind = kind, Taker = took[0].Player, Victim = vct.Player, Lost = Lost(c, g.Members.Count, vct.Payout) });
                    }
                    continue;
                }
                // (a result without groups: a hand-made one) a sole taker gets the taker share, the partners who shared get
                // the sharer share
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
