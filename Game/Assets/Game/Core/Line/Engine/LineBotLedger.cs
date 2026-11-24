using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// A bot's private estimate of everyone's prim (the "defter", docs/V5_BOT_TALK.md §6), built only from what the bot's
    /// own <see cref="LineView"/>s show. Balances are hidden, so the bot keeps the facts it saw across phases (the meeting
    /// dossier is per meeting) and turns them into an estimate when it needs one:
    /// <list type="bullet">
    /// <item>everyone starts at <see cref="LineConfig.StartPrim"/>; fired players count as 0; my own balance is exact;</item>
    /// <item>the task amounts are this match's ("Haftanın gündemi": the view's <see cref="LineStakesView"/>);</item>
    /// <item>Client Bonus: my own group exactly; the others get the expected payout from the public takers count, the
    ///   unknown takers spread over the colleagues not known either way, weighted toward known Petty Cash thieves. An
    ///   opened Client Bonus file (public) says its target took and its holder shared (legacy rules: an investigation's
    ///   result, known either way);</item>
    /// <item>Petty Cash: caught takers get 0; an opened Petty Cash file says its target is a thief who keeps the cash and
    ///   its holder a witness (legacy rules: the whistle; v5.0: the thief nets 0 and the whistler earns the reward); my
    ///   own group exactly; the unresolved thefts of other groups (the meeting dossier's count, less the files opened
    ///   since) spread over the colleagues who could have taken, weighted by their Client Bonus take probability;</item>
    /// <item>Weekend Shift: everyone not on the public roster gets <see cref="LineConfig.ShiftSkipPay"/> when the shift was
    ///   staffed, <see cref="LineConfig.ShiftFailSkipPay"/> (v5.1: 0) when it was not;</item>
    /// <item>legacy investigations: the investigator pays the cost and, when the target was guilty, gets the refund from the
    ///   target (v5.1: both 0; opening a file never moves money);</item>
    /// <item>desk shares of the firings so far;</item>
    /// <item>my own transfers (I sent X to j: j +X; j sent me X: j −X), silence money and its returns included;</item>
    /// <item>pranks in the public log: the prim it burned, from its actor (a phone ring's actor is hidden until the results:
    ///   skipped).</item>
    /// </list>
    /// Ingesting a view only records facts (idempotent: the same view twice changes nothing), so a talk preview may feed it
    /// without changing a decision. A new week starts a fresh ledger.
    /// </summary>
    public sealed class LineBotLedger
    {
        const int N = LineProtocol.MaxPlayers;

        readonly int me;
        int week = int.MinValue;
        readonly bool[] seated = new bool[N], fired = new bool[N];
        int myPrim = -1;

        // Client Bonus (my group from my own reveal; the takers count is public)
        bool cbKnown;
        readonly bool[] cbGroup = new bool[N];
        readonly int[] cbChoice = new int[N];
        int cbTakers = -1;

        // Petty Cash (my group from my own reveal; caught, whistles and the open count from the meeting 1 dossier)
        bool pcKnown;
        readonly bool[] pcGroup = new bool[N], pcCaught = new bool[N];
        readonly int[] pcChoice = new int[N];
        /// <summary>Per thief: who opened the file on them / whistled (-1 none).</summary>
        readonly int[] pcWhistler = new int[N];
        int pcOpen = -1;
        /// <summary>Files (public): Client Bonus files opened on a seat / by a seat; when a Petty Cash file was opened on
        /// a thief; when the meeting dossier counted the unresolved thefts.</summary>
        readonly bool[] cbOpened = new bool[N], cbOpener = new bool[N];
        readonly double[] pcOpenedAt = new double[N];
        double pcMissingTime = double.NaN;
        /// <summary>This match's amounts (the view's stakes; -1 = the config's).</summary>
        int stShare = -1, stTaker = -1, stCash = -1, stSkip = -1;

        // Weekend Shift (public roster)
        bool wsKnown;
        readonly bool[] wsRoster = new bool[N];

        // Investigations (public): per investigator, the target (-1 none) and whether they were guilty
        readonly int[] invTarget = new int[N];
        readonly bool[] invGuilty = new bool[N];

        // Firings (public desk reveal)
        readonly int[] desk = new int[N];
        readonly bool[] firingRound = new bool[8];

        // My transfers and the prank log: recomputed from the full lists on every ingest
        readonly int[] transferNet = new int[N];
        readonly int[] prankSpent = new int[N];
        int transfersSeen = -1, pranksSeen = -1;

        public LineBotLedger(int me)
        {
            if (me < 0 || me >= N) throw new ArgumentException("Invalid seat.");
            this.me = me;
            Reset(int.MinValue);
        }

        void Reset(int newWeek)
        {
            week = newWeek;
            myPrim = -1;
            cbKnown = pcKnown = wsKnown = false;
            cbTakers = pcOpen = -1;
            transfersSeen = pranksSeen = -1;
            pcMissingTime = double.NaN;
            stShare = stTaker = stCash = stSkip = -1;
            for (int i = 0; i < N; i++)
            {
                seated[i] = fired[i] = cbGroup[i] = pcGroup[i] = pcCaught[i] = wsRoster[i] = invGuilty[i] = false;
                cbChoice[i] = pcChoice[i] = pcWhistler[i] = invTarget[i] = -1;
                desk[i] = transferNet[i] = prankSpent[i] = 0;
                cbOpened[i] = cbOpener[i] = false;
                pcOpenedAt[i] = double.NaN;
            }
            for (int i = 0; i < firingRound.Length; i++) firingRound[i] = false;
        }

        static bool Seat(int id) { return id >= 0 && id < N; }

        /// <summary>Records what <paramref name="v"/> (this seat's own view) shows. Other seats' views are ignored.</summary>
        public void Ingest(LineView v)
        {
            if (v == null || v.Me != me) return;
            if (v.WeekId != week) Reset(v.WeekId);
            foreach (var p in v.Players)
            {
                if (!Seat(p.Id)) continue;
                seated[p.Id] = true;
                if (p.Fired) fired[p.Id] = true;
            }
            if (v.Mine != null) myPrim = v.Mine.Prim;
            var st = v.Stakes;
            if (st != null && st.Scheme == LineStakes.Permutation) { stShare = st.BonusShare; stTaker = st.BonusTaker; stCash = st.CashTake; stSkip = st.ShiftSkipPay; }
            var t = v.Task;
            if (t != null && t.Revealed)
            {
                if (t.Kind == TaskKind.ClientBonus)
                {
                    cbKnown = true;
                    foreach (int id in t.Group) if (Seat(id)) cbGroup[id] = true;
                    foreach (var c in t.Choices) if (Seat(c.Player)) { cbGroup[c.Player] = true; cbChoice[c.Player] = c.Choice; }
                    if (t.Takers >= 0) cbTakers = t.Takers;
                }
                else if (t.Kind == TaskKind.PettyCash)
                {
                    pcKnown = true;
                    foreach (int id in t.Group) if (Seat(id)) pcGroup[id] = true;
                    foreach (var c in t.Choices) if (Seat(c.Player)) { pcGroup[c.Player] = true; pcChoice[c.Player] = c.Choice; }
                }
                else if (t.Kind == TaskKind.WeekendShift)
                {
                    wsKnown = true;
                    for (int i = 0; i < N; i++) wsRoster[i] = false;
                    foreach (int id in t.Roster) if (Seat(id)) wsRoster[id] = true;
                }
            }
            var m = v.Meeting;
            if (m != null)
            {
                bool sawCash = false;
                foreach (var d in m.Dossier)
                {
                    switch (d.Kind)
                    {
                        case DossierKind.BonusTakers: if (d.N1 >= 0) cbTakers = d.N1; break;
                        case DossierKind.CashCaught: if (Seat(d.Player)) pcCaught[d.Player] = true; sawCash = true; break;
                        case DossierKind.Whistle: if (Seat(d.Other)) pcWhistler[d.Other] = d.Player; sawCash = true; break;
                        case DossierKind.CashMissing: pcOpen = Math.Max(0, d.N2); pcMissingTime = d.Time; sawCash = true; break;
                        case DossierKind.ShiftRoster:
                            wsKnown = true;
                            for (int i = 0; i < N; i++) wsRoster[i] = false;
                            foreach (int id in m.Roster) if (Seat(id)) wsRoster[id] = true;
                            break;
                        case DossierKind.Investigation:
                            if (Seat(d.Player) && Seat(d.Other)) { invTarget[d.Player] = d.Other; invGuilty[d.Player] = d.N1 == 1; }
                            break;
                    }
                }
                // meeting 1 lists the unresolved thefts only when there are some: no CashMissing line = none open
                if (pcOpen < 0 && (sawCash || m.Index == 0 && pcKnown)) pcOpen = 0;
            }
            // opened files are public at once, in every phase
            if (v.Files != null)
                foreach (var f in v.Files.Opened)
                {
                    if (f == null || !Seat(f.Target) || !Seat(f.Holder)) continue;
                    if (f.Task == TaskKind.ClientBonus) { cbOpened[f.Target] = true; cbOpener[f.Holder] = true; }
                    else if (f.Task == TaskKind.PettyCash) { pcWhistler[f.Target] = f.Holder; pcOpenedAt[f.Target] = f.Time; }
                }
            if (v.Final != null)
                foreach (var f in v.Final.Firings)
                {
                    if (f.Round < 0 || f.Round >= firingRound.Length || firingRound[f.Round]) continue;
                    firingRound[f.Round] = true;
                    if (Seat(f.Player)) fired[f.Player] = true;
                    foreach (int r in f.Recipients) if (Seat(r)) desk[r] += f.Share;
                }
            // my transfers: the full list (it only grows)
            var tr = v.Mine != null ? v.Mine.Transfers : null;
            if (tr != null && tr.Count >= transfersSeen)
            {
                transfersSeen = tr.Count;
                for (int i = 0; i < N; i++) transferNet[i] = 0;
                foreach (var x in tr)
                {
                    if (x.From == me && Seat(x.To) && x.To != me) transferNet[x.To] += x.Amount;
                    else if (x.To == me && Seat(x.From) && x.From != me) transferNet[x.From] -= x.Amount;
                }
            }
            // the public prank log: the prim each known actor burned (a phone ring's actor is hidden until the results)
            var pv = v.Pranks;
            if (pv != null && pv.Log.Count >= pranksSeen)
            {
                pranksSeen = pv.Log.Count;
                for (int i = 0; i < N; i++) prankSpent[i] = 0;
                foreach (var e in pv.Log) if (Seat(e.Actor) && e.Cost > 0) prankSpent[e.Actor] += e.Cost;
            }
        }

        static int Payout(bool[] group, int[] choice, int id, LineConfig c, int share, int taker)
        {
            int takers = 0, size = 0;
            for (int g = 0; g < N; g++)
            {
                if (!group[g]) continue;
                size++;
                if (choice[g] == LineChoice.Take) takers++;
            }
            bool take = choice[id] == LineChoice.Take;
            if (size <= 2) return takers == 0 ? (share >= 0 ? share : c.BonusPairShare) : takers >= 2 ? c.BonusPairBothTake : take ? (taker >= 0 ? taker : c.BonusPairTaker) : c.BonusPairSharer;
            return takers == 0 ? (share >= 0 ? share : c.BonusTrioNone) : takers >= 2 ? c.BonusTrioMany : take ? (taker >= 0 ? taker : c.BonusTrioTaker) : c.BonusTrioOthers;
        }

        /// <summary>The estimated prim per seat id (index = seat; unseated seats 0). Fired = 0, mine = my real balance.</summary>
        public double[] Estimate(LineConfig c)
        {
            if (c == null) c = LineConfig.CreateDefault();
            var est = new double[N];
            for (int id = 0; id < N; id++) if (seated[id]) est[id] = c.StartPrim;
            // this match's amounts ("Haftanın gündemi"), whatever config the caller holds
            int pairShare = stShare >= 0 ? stShare : c.BonusPairShare, pairTaker = stTaker >= 0 ? stTaker : c.BonusPairTaker;
            int cashTake = stCash >= 0 ? stCash : c.CashTake, shiftSkipPay = stSkip >= 0 ? stSkip : c.ShiftSkipPay;
            var old = c.Legacy;
            bool thiefKeeps = old == null || old.CashWhistledKeeps;
            int whistleReward = old != null ? old.CashWhistleReward : 0;

            // Client Bonus: the probability that each colleague outside my group took
            var pTake = new double[N];
            var hasP = new bool[N];
            if (cbKnown && cbTakers >= 0)
            {
                int known = 0;
                var unknown = new List<int>();
                for (int id = 0; id < N; id++)
                {
                    if (!seated[id]) continue;
                    if (cbGroup[id])
                    {
                        bool took = cbChoice[id] == LineChoice.Take;
                        pTake[id] = took ? 1 : 0;
                        hasP[id] = true;
                        if (took) known++;
                        continue;
                    }
                    int verdict = InvestigatedAs(id);
                    if (verdict >= 0)
                    {
                        pTake[id] = verdict;
                        hasP[id] = true;
                        if (verdict == 1) known++;
                    }
                    else unknown.Add(id);
                }
                int k = Math.Max(0, cbTakers - known);
                double sw = 0;
                var w = new double[N];
                foreach (int id in unknown) { w[id] = 1 + (KnownCashThief(id) ? 1.5 : 0); sw += w[id]; }
                double pbar = 0;
                foreach (int id in unknown)
                {
                    pTake[id] = sw > 0 ? Math.Min(1, k * w[id] / sw) : 0;
                    hasP[id] = true;
                    pbar += pTake[id];
                }
                pbar = unknown.Count > 0 ? pbar / unknown.Count : 0;
                for (int id = 0; id < N; id++)
                {
                    if (!seated[id]) continue;
                    if (cbGroup[id]) { est[id] += Payout(cbGroup, cbChoice, id, c, stShare, stTaker); continue; }
                    // an opened Client Bonus file: its target took from a sharer (a pair's sole taker; a trio's second taker
                    // is rare and not told apart), and its holder was left with the sharer's amount
                    if (cbOpened[id]) { est[id] += pairTaker; continue; }
                    if (cbOpener[id]) { est[id] += c.BonusPairSharer; continue; }
                    // (groups I was not in: the pair payouts, the partner taking with the average probability)
                    double p = pTake[id], q = pbar;
                    est[id] += p * (q * c.BonusPairBothTake + (1 - q) * pairTaker) + (1 - p) * (q * c.BonusPairSharer + (1 - q) * pairShare);
                }
            }

            // Petty Cash
            if (pcKnown)
            {
                int mySoleThief = SoleTaker(pcGroup, pcChoice);
                var candidates = new List<int>();
                for (int id = 0; id < N; id++)
                {
                    if (!seated[id]) continue;
                    est[id] += Whistles(id) * whistleReward;
                    if (pcWhistler[id] >= 0 && !pcCaught[id] && thiefKeeps) { est[id] += cashTake; continue; } // file opened / whistled: keeps it
                    if (pcCaught[id] || pcWhistler[id] >= 0) continue; // caught: 0; whistled (v5.0): +take −take
                    if (pcGroup[id])
                    {
                        if (id == mySoleThief) est[id] += cashTake;
                        continue;
                    }
                    if (Whistles(id) > 0) continue; // whoever opened the file / whistled was a witness, not a taker
                    candidates.Add(id);
                }
                // the unresolved thefts of the other groups (my group's open theft is counted in the dossier too)
                int open = pcOpen - (mySoleThief >= 0 && pcWhistler[mySoleThief] < 0 ? 1 : 0);
                // a file opened after the dossier counted the thefts settled one of them
                if (!double.IsNaN(pcMissingTime))
                    for (int id = 0; id < N; id++) if (!double.IsNaN(pcOpenedAt[id]) && pcOpenedAt[id] >= pcMissingTime - 1e-9) open--;
                if (open > 0 && candidates.Count > 0)
                {
                    double sv = 0;
                    var vw = new double[N];
                    foreach (int id in candidates) { vw[id] = 0.5 + (hasP[id] ? pTake[id] : 0.3); sv += vw[id]; }
                    foreach (int id in candidates) est[id] += cashTake * Math.Min(1, open * vw[id] / sv);
                }
            }

            // Weekend Shift: the skippers' pay depends on whether the roster reached ceil(N/2)
            if (wsKnown)
            {
                int players = 0, volunteers = 0;
                for (int id = 0; id < N; id++) { if (seated[id]) players++; if (seated[id] && wsRoster[id]) volunteers++; }
                int skipPay = volunteers < LineConfig.ShiftNeeded(players) ? c.ShiftFailSkipPay : shiftSkipPay;
                for (int id = 0; id < N; id++) if (seated[id] && !wsRoster[id]) est[id] += skipPay;
            }

            // Investigations (legacy rules; opening a file moves no money)
            if (old != null)
                for (int id = 0; id < N; id++)
                {
                    int target = invTarget[id];
                    if (target < 0) continue;
                    est[id] -= old.InvestigationCost;
                    if (invGuilty[id]) { est[id] += old.InvestigationRefund; est[target] -= old.InvestigationRefund; }
                }

            for (int id = 0; id < N; id++)
            {
                if (!seated[id]) continue;
                est[id] += desk[id] + transferNet[id] - prankSpent[id];
                if (fired[id]) est[id] = 0;
                est[id] = Math.Max(0, est[id]);
            }
            if (myPrim >= 0 && seated[me] && !fired[me]) est[me] = myPrim;
            return est;
        }

        /// <summary>1 = this colleague took the Client Bonus (a file was opened on them; legacy: a guilty investigation),
        /// 0 = shared (they opened a Client Bonus file; legacy: an innocent investigation), -1 = unknown.</summary>
        int InvestigatedAs(int id)
        {
            if (cbOpened[id]) return 1;
            if (cbOpener[id]) return 0;
            for (int i = 0; i < N; i++) if (invTarget[i] == id) return invGuilty[i] ? 1 : 0;
            return -1;
        }

        bool KnownCashThief(int id)
        {
            if (pcCaught[id] || pcWhistler[id] >= 0) return true;
            return pcGroup[id] && pcChoice[id] == LineChoice.Take;
        }

        int Whistles(int id)
        {
            int n = 0;
            for (int i = 0; i < N; i++) if (pcWhistler[i] == id) n++;
            return n;
        }

        static int SoleTaker(bool[] group, int[] choice)
        {
            int thief = -1, n = 0;
            for (int i = 0; i < N; i++) if (group[i] && choice[i] == LineChoice.Take) { thief = i; n++; }
            return n == 1 ? thief : -1;
        }
    }
}
