using System;
using System.Collections.Generic;
using System.Globalization;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// Yaramazlıklar for bots (docs/V5_YARAMAZLIKLAR.md §7). The talk layer, not <see cref="LineBot"/>, decides a bot's
    /// pranks, only from its own view: Honest never throws anything at anyone (it slurps, and flatters the manager when his
    /// face is red); Greedy throws coffee at whoever accused it or leaked its call when it has prim to spare, and when the
    /// manager is red rings the phone of the colleague it votes for (the tutanak lands on them); Mixed sticks a "YALANCI" or
    /// "HIRSIZ?" post-it on an accuser or a culprit it knows, or sends them a paper plane. Everyone sometimes caps the
    /// results with a cake for the winner. No bot does a prank that would bring itself a tutanak (only Greedy's phone ring
    /// overflows the patience, onto its target); a sealed candidate never spends prim; at most two pranks per session; no
    /// pranks during the votes (a tutanak there could change a vote the bot has already announced). Every prank carries the
    /// patience the bot saw (the engine's guard), so a colleague's prank landing first can never make a bot overflow it.
    /// Bots react to pranks on them with a line over their head (<see cref="BotTalkOutput.Shouts"/>).
    /// Decisions are seeded hashes of the match and the moment, so the same seed gives the same pranks.
    /// </summary>
    public sealed partial class LineBotTalker
    {
        const int MaxPranksPerSession = 2;

        /// <summary>Colleagues who accused me in the meeting chat this match (a bot line naming me or my name in their text).</summary>
        readonly HashSet<int> accusers = new HashSet<int>();
        int prankSession = int.MinValue, prankCount, prankSlot, prankLogSeen;
        double prankPhaseKey = double.NaN, prankAt, nextShoutAt;
        readonly List<Due> shouts = new List<Due>();
        readonly HashSet<int> cakesLogged = new HashSet<int>();
        int prankAttempts, reactions;

        void ResetPranks()
        {
            accusers.Clear();
            prankSession = int.MinValue;
            prankCount = prankSlot = prankLogSeen = 0;
            prankPhaseKey = double.NaN;
            prankAt = nextShoutAt = 0;
            shouts.Clear();
            cakesLogged.Clear();
            prankAttempts = reactions = 0;
        }

        static bool VotePhase(LinePhase p) { return p == LinePhase.MeetingVote || p == LinePhase.CrisisVote || p == LinePhase.FinalVote; }

        /// <summary>Prank moments in a phase: the long discussions and the results get two.</summary>
        static int PrankSlots(LineView v)
        {
            return v.Phase == LinePhase.MeetingDiscussion || v.Phase == LinePhase.FinalDiscussion || v.Phase == LinePhase.Results ? 2 : 1;
        }

        /// <summary>When this seat may prank in the phase: spread over the phase by seat order (the bots take turns, so each
        /// one decides on a fresh view), with a seeded nudge.</summary>
        double PrankMoment(LineView v, int slot)
        {
            int slots = PrankSlots(v), n = Math.Max(1, v.Players.Count);
            double len = Math.Max(0.5, v.PhaseEndTime - v.PhaseStartTime);
            double within = (BotRank(v) + 0.15 + 0.6 * U("pkat" + (int)v.Phase + ":" + v.Day + ":" + slot)) / n;
            return v.PhaseStartTime + len * (0.12 + 0.76 * (slot + within) / slots);
        }

        void Pranks(LineView v, double now, BotTalkOutput o)
        {
            var pv = v.Pranks;
            if (pv == null) return;
            Reactions(v, pv, now, o);
            if (v.Phase == LinePhase.Results) LogCakes(v, pv, o);
            if (!v.Features.PranksOpen || pv.Session < 0) return;
            if (pv.Session != prankSession) { prankSession = pv.Session; prankCount = 0; }
            if (v.PhaseStartTime != prankPhaseKey)
            {
                prankPhaseKey = v.PhaseStartTime;
                prankSlot = 0;
                prankAt = PrankMoment(v, 0);
            }
            if (prankSlot >= PrankSlots(v) || now < prankAt) return;
            int slot = prankSlot++;
            if (prankSlot < PrankSlots(v)) prankAt = PrankMoment(v, prankSlot);
            if (prankCount >= MaxPranksPerSession || VotePhase(v.Phase)) return;
            var a = ChoosePrank(v, pv, slot);
            if (a == null) return;
            prankCount++;
            prankAttempts++;
            o.Actions.Add(a);
        }

        LinePrankOptionView Option(LinePranksView pv, PrankKind kind)
        {
            foreach (var o in pv.Options) if (o.Kind == kind) return o;
            return null;
        }

        /// <summary>A prank the bot may do now: open, and never a prim prank from a sealed desk.</summary>
        bool Ready(LineView v, LinePranksView pv, PrankKind kind)
        {
            var o = Option(pv, kind);
            if (o == null || o.Block != LineFailure.None) return false;
            var me = P(v, Seat);
            return o.Cost == 0 || (me != null && !me.Candidate && !me.Fired);
        }

        /// <summary>The prank leaves the manager's patience below its capacity (no tutanak for me).</summary>
        bool Safe(LinePranksView pv, PrankKind kind)
        {
            var o = Option(pv, kind);
            return o != null && pv.Patience + o.Patience < config.PatienceCapacity;
        }

        LineAction PrankAction(LineView v, LinePranksView pv, PrankKind kind, int target, int word = -1, string note = "")
        {
            int guard = kind == PrankKind.Cake ? -1 : pv.Patience;
            return new LineAction(v.WeekId, ++request, LineVerb.Prank, target, (int)kind, word, guard, note ?? "");
        }

        LineAction ChoosePrank(LineView v, LinePranksView pv, int slot)
        {
            string topic = "prank:" + pv.Session + ":" + (int)v.Phase + ":" + slot + ":" + prankCount;
            if (pv.Session == LinePranks.SessionResults)
            {
                // everyone sometimes caps the results with a cake for the winner
                if (v.Results == null || !Ready(v, pv, PrankKind.Cake) || U("cake?" + topic) >= 0.55) return null;
                var winners = new List<int>();
                foreach (int w in v.Results.Winners) if (w != Seat) winners.Add(w);
                if (winners.Count == 0) return null;
                winners.Sort();
                return PrankAction(v, pv, PrankKind.Cake, winners[(int)(U("cakewho" + topic) * winners.Count) % winners.Count]);
            }
            switch (Policy)
            {
                case LineBotPolicy.Honest:
                    // never at anyone: a loud slurp, and some sweet talk when the manager is red
                    if (pv.Level >= 2 && Ready(v, pv, PrankKind.Flatter) && U("flatter" + topic) < 0.85) return PrankAction(v, pv, PrankKind.Flatter, -1);
                    if (Ready(v, pv, PrankKind.Slurp) && Safe(pv, PrankKind.Slurp) && U("slurp" + topic) < 0.45) return PrankAction(v, pv, PrankKind.Slurp, -1);
                    return null;
                case LineBotPolicy.Greedy:
                {
                    // red: ring the phone of the colleague I vote for (the manager blames them)
                    if (pv.Level >= 2 && Ready(v, pv, PrankKind.PhoneRing) && U("ring" + topic) < 0.75)
                    {
                        int target = VoteTarget(v);
                        if (target >= 0 && target != Seat) return PrankAction(v, pv, PrankKind.PhoneRing, target);
                    }
                    // coffee for whoever accused me or leaked my call, when I have prim to spare
                    int grudge = Pick(Grudges(v), "grudge" + topic);
                    var coffee = Option(pv, PrankKind.Coffee);
                    if (grudge >= 0 && coffee != null && v.Mine.Prim >= coffee.Cost + 6 && Ready(v, pv, PrankKind.Coffee) && Safe(pv, PrankKind.Coffee)
                        && U("coffee" + topic) < 0.6)
                        return PrankAction(v, pv, PrankKind.Coffee, grudge);
                    return null;
                }
                default:
                {
                    // Mixed: a post-it ("YALANCI" for an accuser, "HIRSIZ?" for a culprit I know) or a paper plane
                    var grudges = Grudges(v);
                    var culprits = KnownCulprits(v);
                    var pool = new List<int>(grudges);
                    foreach (int id in culprits) if (!pool.Contains(id) && id != Seat) pool.Add(id);
                    int target = Pick(pool, "mxwho" + topic);
                    if (target < 0) return null;
                    bool accuser = grudges.Contains(target);
                    if (U("mxkind" + topic) < 0.5)
                    {
                        if (Ready(v, pv, PrankKind.PostIt) && Safe(pv, PrankKind.PostIt) && U("postit" + topic) < 0.55)
                            return PrankAction(v, pv, PrankKind.PostIt, target, accuser ? LinePranks.WordLiar : LinePranks.WordThief);
                        return null;
                    }
                    if (Ready(v, pv, PrankKind.Plane) && Safe(pv, PrankKind.Plane) && U("plane" + topic) < 0.55)
                    {
                        var note = Line(U("note" + topic) < 0.5 ? "bot.prank.note.m1" : "bot.prank.note.m2", target);
                        return PrankAction(v, pv, PrankKind.Plane, target, -1, LineBotLines.Token(note));
                    }
                    return null;
                }
            }
        }

        int Pick(List<int> from, string topic)
        {
            if (from.Count == 0) return -1;
            from.Sort();
            return from[(int)(U(topic) * from.Count) % from.Count];
        }

        /// <summary>Who wronged me in public: accusers in the chat, the whistle / investigation against me, a leak of my call.
        /// (Ballots stay secret until the results, so "who voted for me" is not known to anyone.)</summary>
        List<int> Grudges(LineView v)
        {
            var set = new HashSet<int>(accusers);
            if (v.Meeting != null)
                foreach (var d in v.Meeting.Dossier)
                {
                    if ((d.Kind == DossierKind.Whistle || d.Kind == DossierKind.Investigation) && d.Other == Seat && d.Player >= 0) set.Add(d.Player);
                    if (d.Kind == DossierKind.Leak && (d.N1 == Seat || d.N2 == Seat) && d.Player >= 0) set.Add(d.Player);
                }
            set.Remove(Seat);
            var list = new List<int>();
            foreach (int id in set) if (P(v, id) != null) list.Add(id);
            return list;
        }

        /// <summary>The colleague I vote (or will vote) for: my ballot when it is in, else the bot's own preview.</summary>
        int VoteTarget(LineView v)
        {
            if (v.Mine.Ballots.Count > 0 && v.Mine.Ballots[0] >= 0) return v.Mine.Ballots[0];
            if (bot == null) return -1;
            bool final = v.Phase >= LinePhase.FinalDiscussion && v.Phase <= LinePhase.FinalVerdict && v.Final != null;
            if (!final) return bot.PlannedVote(v, false, 0);
            var options = new List<int>();
            foreach (int id in v.Final.Candidates) { var p = P(v, id); if (p != null && !p.Fired && id != Seat) options.Add(id); }
            return options.Count == 0 ? -1 : bot.PlannedVote(v, true, 0, options);
        }

        // ------------------------------------------------------------------ reactions (over my head, with the mumble)

        void Reactions(LineView v, LinePranksView pv, double now, BotTalkOutput o)
        {
            for (int i = prankLogSeen; i < pv.Log.Count; i++)
            {
                var e = pv.Log[i];
                string stem = null;
                double delay = 1.2;
                if (e.Target == Seat)
                    switch (e.Kind)
                    {
                        case PrankKind.Coffee: stem = "bot.prank.coffee"; delay = 1.4; break;
                        case PrankKind.PostIt: stem = "bot.prank.postit"; delay = 1.0; break;
                        case PrankKind.PhoneRing: stem = "bot.prank.phone"; delay = 2.0; break;
                        case PrankKind.Plane: stem = "bot.prank.plane"; delay = 1.6; break;
                        case PrankKind.Cake: stem = "bot.prank.cake"; delay = 1.2; break;
                    }
                // a tutanak for my own prank (a phone ring's tutanak is what the phone line answers)
                if (e.Tutanak && e.Culprit == Seat && e.Kind != PrankKind.PhoneRing) { stem = "bot.prank.tutanak"; delay = 1.8; }
                if (stem == null || shouts.Count >= 3) continue;
                var l = Line(Key(stem), -1);
                l.Shout = true;
                shouts.Add(new Due { At = Math.Max(now, nextShoutAt) + delay, Line = l });
            }
            prankLogSeen = pv.Log.Count;
            if (shouts.Count == 0 || shouts[0].At > now) return;
            var due = shouts[0].Line;
            shouts.RemoveAt(0);
            if (!LineBotLines.Valid(due)) return;
            Said(due, now, o);
            o.Shouts.Add(due);
            reactions++;
            nextShoutAt = now + 2.5;
        }

        // ------------------------------------------------------------------ the log (released at Results)

        void FinishPranks(LineView v)
        {
            var pv = v.Pranks;
            if (pv == null) return;
            int made = 0, tutanak = 0, spent = 0;
            foreach (var e in pv.Log)
            {
                if (e.Actor != Seat) continue;
                made++;
                spent += e.Cost;
                if (e.Tutanak) tutanak++;
                pendingLog.Add(PrankLine(v, e));
            }
            int final = -1;
            if (v.Results != null) foreach (var p in v.Results.Players) if (p.Player == Seat) final = p.Prim;
            Log(string.Format(CultureInfo.InvariantCulture, "pranks seat={0} policy={1} made={2} tried={3} tutanak={4} spent={5} reactions={6} final_prim={7}",
                Seat, Policy, made, prankAttempts, tutanak, spent, reactions, final));
        }

        /// <summary>Cakes come during the results, after the log was released: logged as they land.</summary>
        void LogCakes(LineView v, LinePranksView pv, BotTalkOutput o)
        {
            foreach (var e in pv.Log)
                if (e.Actor == Seat && e.Kind == PrankKind.Cake && cakesLogged.Add(e.Index)) o.Log.Add(PrankLine(v, e));
        }

        string PrankLine(LineView v, LinePrankEntryView e)
        {
            bool candidate = false;
            int prim = config.StartPrim;
            var r = v.Results;
            if (r != null)
            {
                foreach (var p in r.Players) if (p.Player == Seat) candidate = p.WasCandidate;
                foreach (var c in r.PrimLog) if (c.Player == Seat && c.Time <= e.Time) prim += c.Delta;
            }
            bool sealedDesk = candidate && e.Phase >= LinePhase.CrisisVerdict && e.Phase <= LinePhase.Results;
            return string.Format(CultureInfo.InvariantCulture,
                "bottalk prank seat={0} policy={1} kind={2} target={3} session={4} phase={5} cost={6} patience={7} tutanak={8} culprit={9} sealed={10} prim_after={11} t={12:0.0}",
                Seat, Policy, e.Kind, e.Target, e.Session, e.Phase, e.Cost, e.Patience, Yes(e.Tutanak), e.Culprit, Yes(sealedDesk), r != null ? prim : -1, e.Time);
        }
    }
}
