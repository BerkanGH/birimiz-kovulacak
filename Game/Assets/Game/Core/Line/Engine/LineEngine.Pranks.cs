using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// Yaramazlıklar (docs/V5_YARAMAZLIKLAR.md, docs/V5_ENGINE_SPEC.md §19): meeting room pranks and the manager's patience.
    /// Pranks never block voice, votes, the defence order or any other rule; their only effects are burned prim and the
    /// tutanak (+suç) of whoever overflows the patience. Sessions: meeting 1, meeting 2, the crisis vote (vote + verdict),
    /// the final (discussion .. last verdict); Results allows only the cake. Every session starts with patience 0, no
    /// post-its and fresh per-session limits; coffee stains stay for the whole match. No randomness is used here.
    /// </summary>
    internal sealed partial class LineEngine
    {
        private sealed class PrankEntry
        {
            internal int Index;
            internal PrankKind Kind;
            internal int Actor, Target = -1, Word = -1;
            internal string Note = "";
            internal int Session;
            internal LinePhase Phase;
            internal double Time;
            internal int Cost, Patience, PatienceAfter, Culprit = -1;
            internal bool Tutanak;
        }

        private const int KindSlots = LinePranks.KindCount + 1;
        private readonly List<PrankEntry> prankLog = new List<PrankEntry>();
        private int prankSession = LinePranks.SessionNone;
        private int patience;
        private readonly int[] postIts = { -1, -1, -1, -1, -1, -1 };
        private readonly int[] stains = new int[LineProtocol.MaxPlayers];
        private readonly int[] cakesIn = new int[LineProtocol.MaxPlayers];
        private readonly int[] cakesThrown = new int[LineProtocol.MaxPlayers];
        private readonly int[][] prankUsed = NewTable<int>();
        private readonly double[][] prankReady = NewTable<double>();

        private static T[][] NewTable<T>()
        {
            var t = new T[LineProtocol.MaxPlayers][];
            for (int i = 0; i < t.Length; i++) t[i] = new T[KindSlots];
            return t;
        }

        private bool PranksOn { get { return c.PranksEnabled && setup.PranksEnabled; } }

        /// <summary>The current phase's prank session (-1 when pranks are off or outside the meeting room).</summary>
        private int PrankSessionNow { get { return PranksOn && !IsFinished ? LinePranks.SessionOf(Phase, Day) : LinePranks.SessionNone; } }

        private int PatienceMultiplier { get { return Phase == LinePhase.FinalDefense ? Math.Max(1, c.DefencePatienceMultiplier) : 1; } }

        /// <summary>Called on every phase change: a new session clears the patience, the post-its and the per-session uses.</summary>
        private void PrankPhase()
        {
            int session = LinePranks.SessionOf(Phase, Day);
            if (session == prankSession) return;
            prankSession = session;
            patience = 0;
            for (int i = 0; i < postIts.Length; i++) postIts[i] = -1;
            foreach (var row in prankUsed) Array.Clear(row, 0, row.Length);
        }

        /// <summary>Why <paramref name="id"/> cannot do <paramref name="kind"/> now, whatever the target (None = allowed).</summary>
        private LineFailure PrankBlock(int id, PrankKind kind, double time)
        {
            if (!PranksOn) return LineFailure.NotAvailable;
            int session = PrankSessionNow;
            if (session == LinePranks.SessionNone) return LineFailure.WrongPhase;
            if (session == LinePranks.SessionResults) { if (kind != PrankKind.Cake) return LineFailure.WrongPhase; }
            else if (kind == PrankKind.Cake) return LineFailure.WrongPhase;
            var s = seats[id];
            if (s.Fired && session != LinePranks.SessionResults) return LineFailure.Fired;
            int cost = c.PrankCost(kind);
            // Sealed desk: a candidate cannot burn the prim the voters are to share (free pranks are fine)
            if (cost > 0 && s.Candidate) return LineFailure.DeskSealed;
            int limit = c.PrankPerSession(kind);
            if (limit > 0 && prankUsed[id][(int)kind] >= limit) return LineFailure.AlreadyUsed;
            if (kind == PrankKind.Cake && cakesThrown[id] >= c.CakeMax) return LineFailure.LimitReached;
            if (time < prankReady[id][(int)kind]) return LineFailure.RateLimited;
            if (s.Prim < cost) return LineFailure.InsufficientFunds;
            return LineFailure.None;
        }

        private LineFailure Prank(int id, LineAction a, double time)
        {
            if (!PranksOn) return LineFailure.NotAvailable;
            var kind = (PrankKind)Math.Max(0, Math.Min(255, a.A));
            if (a.A < 0 || a.A > 255 || !LinePranks.Valid(kind)) return LineFailure.BadParam;
            int session = PrankSessionNow;
            if (session == LinePranks.SessionNone || (session == LinePranks.SessionResults) != (kind == PrankKind.Cake)) return LineFailure.WrongPhase;
            if (seats[id].Fired && session != LinePranks.SessionResults) return LineFailure.Fired;
            // the target: a colleague (StandUp may point at nobody; slurping and flattering have none)
            int target = a.Target;
            if (LinePranks.NeedsTarget(kind) || (kind == PrankKind.StandUp && target != -1))
            {
                var f = TargetFailure(id, target);
                if (f != LineFailure.None) return f;
            }
            else if (target != -1) return LineFailure.InvalidTarget;
            int word = -1;
            string note = "";
            if (kind == PrankKind.PostIt)
            {
                if (a.B < 0 || a.B >= LinePranks.WordCount) return LineFailure.BadParam;
                word = a.B;
            }
            if (kind == PrankKind.Plane)
            {
                note = LineText.Sanitize(a.Text, c.PlaneNoteMaxChars, false, out var fail);
                if (fail != LineFailure.None) return fail;
            }
            var block = PrankBlock(id, kind, time);
            if (block != LineFailure.None) return block;
            // optional guard: the sender decided on the patience it saw (bots never overflow it by accident)
            if (a.C >= 0 && session != LinePranks.SessionResults && a.C != patience) return LineFailure.PatienceChanged;

            int cost = c.PrankCost(kind);
            var e = new PrankEntry
            {
                Index = prankLog.Count, Kind = kind, Actor = id, Target = target, Word = word, Note = note, Session = session, Phase = Phase,
                Time = time, Cost = cost,
            };
            if (cost > 0) AddPrim(id, -cost, PrimReason.Prank, time, target);
            prankUsed[id][(int)kind]++;
            float cooldown = c.PrankCooldown(kind);
            if (cooldown > 0) prankReady[id][(int)kind] = time + cooldown;
            switch (kind)
            {
                case PrankKind.Coffee: stains[target]++; break;
                case PrankKind.PostIt: postIts[target] = word; break;
                case PrankKind.Cake: cakesThrown[id]++; cakesIn[target]++; break;
            }
            prankLog.Add(e);
            bool hidden = kind == PrankKind.PhoneRing;
            // the prank itself (a phone ring names its actor only to the actor until Results)
            if (hidden)
                foreach (int p in ids) Emit(LineEventType.Prank, time, p == id ? id : -1, target, (int)kind, word, recipient: p, extra: e.Index);
            else Emit(LineEventType.Prank, time, id, target, (int)kind, word, extra: e.Index);
            if (session != LinePranks.SessionResults) Patience(e, hidden, time);
            Changed();
            return LineFailure.None;
        }

        /// <summary>The manager's patience: the prank's cost (×2 in the defence) is charged to its culprit; reaching the
        /// capacity is a tutanak for the culprit and the patience starts again from 0. Flattering lowers it (never below 0).</summary>
        private void Patience(PrankEntry e, bool hidden, double time)
        {
            int before = patience, levelBefore = LinePranks.Level(patience, c.PatienceCapacity);
            int actor = hidden ? -1 : e.Actor;
            if (e.Kind == PrankKind.Flatter)
            {
                patience = Math.Max(0, patience - c.FlatterRelief);
                e.Patience = patience - before;
                e.Culprit = -1;
            }
            else
            {
                int add = c.PrankPatience(e.Kind) * PatienceMultiplier;
                e.Culprit = e.Kind == PrankKind.PhoneRing ? e.Target : e.Actor;
                e.Patience = add;
                patience += add;
                if (patience >= Math.Max(1, c.PatienceCapacity))
                {
                    patience = 0;
                    e.Tutanak = true;
                    Emit(LineEventType.Tutanak, time, actor, e.Culprit, (int)e.Kind, c.TutanakBlame);
                    AddBlame(e.Culprit, c.TutanakBlame, BlameReason.Tutanak, time);
                }
            }
            e.PatienceAfter = patience;
            if (patience != before || e.Tutanak)
                Emit(LineEventType.Patience, time, actor, e.Culprit, patience, LinePranks.Level(patience, c.PatienceCapacity), extra: levelBefore);
        }

        // ---- view

        private LinePranksView PranksView(int player, double time)
        {
            if (!PranksOn) return null;
            int session = PrankSessionNow;
            bool results = Phase == LinePhase.Results;
            var v = new LinePranksView
            {
                Session = session, Patience = session >= 0 && session != LinePranks.SessionResults ? patience : 0, Multiplier = PatienceMultiplier,
            };
            v.Level = LinePranks.Level(v.Patience, c.PatienceCapacity);
            for (int i = 0; i < LineProtocol.MaxPlayers; i++)
            {
                v.PostIts.Add(session >= 0 && session != LinePranks.SessionResults ? postIts[i] : -1);
                v.Stains.Add(stains[i]);
                v.Cakes.Add(cakesIn[i]);
            }
            for (int k = 1; k <= LinePranks.KindCount; k++)
            {
                var kind = (PrankKind)k;
                int limit = c.PrankPerSession(kind);
                var o = new LinePrankOptionView
                {
                    Kind = kind, Cost = c.PrankCost(kind), Block = PrankBlock(player, kind, time),
                    Patience = kind == PrankKind.Flatter ? -c.FlatterRelief : c.PrankPatience(kind) * PatienceMultiplier,
                    UsesLeft = kind == PrankKind.Cake ? Math.Max(0, c.CakeMax - cakesThrown[player]) : limit > 0 ? Math.Max(0, limit - prankUsed[player][k]) : -1,
                    ReadyAt = prankReady[player][k] > time ? prankReady[player][k] : 0,
                };
                v.Options.Add(o);
            }
            foreach (var e in prankLog)
            {
                bool hidden = e.Kind == PrankKind.PhoneRing && !results && e.Actor != player;
                bool note = e.Actor == player || e.Target == player;
                v.Log.Add(new LinePrankEntryView
                {
                    Index = e.Index, Kind = e.Kind, Actor = hidden ? -1 : e.Actor, Target = e.Target, Word = e.Word, Note = note ? e.Note : "",
                    Session = e.Session, Phase = e.Phase, Time = e.Time, Cost = e.Cost, Patience = e.Patience, PatienceAfter = e.PatienceAfter,
                    Culprit = e.Culprit, Tutanak = e.Tutanak,
                });
            }
            return v;
        }
    }
}
