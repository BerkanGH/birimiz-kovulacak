using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    public enum LineBotPolicy : byte
    {
        /// <summary>Share, leave, volunteer when needed; punish whenever it can (returning any silence money).</summary>
        Honest = 0,
        /// <summary>Take, take, skip; punish only when the target paid it for silence (and then on a seeded 50% coin).</summary>
        Greedy = 1,
        /// <summary>Honest or greedy, seeded per decision.</summary>
        Mixed = 2,
    }

    /// <summary>How a bot decides to punish (whistle in the Petty Cash break, investigate in meeting 1).</summary>
    public enum LineBotPunishStyle : byte
    {
        /// <summary>
        /// The v5.1 silence deals (docs/V5_BOT_TALK.md §4.1). Honest punishes whenever it is eligible and returns any silence
        /// money the target sent it (a transfer back); Greedy never punishes unless the target paid it, then keeps the money
        /// and still punishes on a seeded 50% coin (betrayal); Mixed acts as Honest or as Greedy per decision (seeded 50/50).
        /// The coins are hashes of the bot's seed, the week, the kind and the target, so <see cref="LineBot.WillPunish"/> can
        /// preview them (the talk) and they never draw from the bot's random stream.
        /// </summary>
        Deals = 0,
        /// <summary>The v5.0 prototype rule, for LineSim --rules old: every witness whistles at once; Greedy (Mixed on a coin
        /// from the bot's random stream, drawn again in each meeting phase) investigates a group-mate it saw take.</summary>
        Legacy = 1,
    }

    /// <summary>How a bot fills its ballots (meeting suç ballots, the crisis vote, the final firing votes).</summary>
    public enum LineBotVoteStyle : byte
    {
        /// <summary>The v5 prototype rule: a culprit the bot saw take, else the highest suç (seeded tie-break).</summary>
        MostBlame = 0,
        /// <summary>Play to win: the rival the bot estimates to be the richest among the options (<see cref="LineBotLedger"/>,
        /// plus a small fixed per-bot noise so bots disagree a little; seeded tie-break). The fired player's desk goes to
        /// the voters, and the richest player wins.</summary>
        Richest = 1,
    }

    /// <summary>
    /// A view-only synthetic player (spec §13). It never calls, texts or uses the recorder. <see cref="Next"/> returns at
    /// most one action per call; call it until it returns null. With <see cref="ThinkSeconds"/> &gt; 0 the bot waits a
    /// seeded delay after each phase starts (see <see cref="WakeAt"/>).
    /// Punishments (<see cref="PunishStyle"/>, default <see cref="LineBotPunishStyle.Deals"/>): whistles and investigations
    /// follow the silence-deal rules; the only transfers the bot itself sends are Honest's returns of silence money
    /// (<see cref="TransferHoldUntil"/> keeps them apart from the talker's payments).
    /// Talk support (docs/V5_BOT_TALK.md): <see cref="PlannedTaskChoice"/> and <see cref="PlannedVote"/> preview a decision
    /// before the bot makes it by running the same policy on a copy of its random state, so they never change what it does.
    /// Calls, chat lines and payments are <see cref="LineBotTalker"/>'s job.
    /// Votes (<see cref="VoteStyle"/>, default <see cref="LineBotVoteStyle.Richest"/>): every policy names the rival it
    /// estimates to be the richest, from its own <see cref="LineBotLedger"/> (fed every view in <see cref="Next"/>). Once the
    /// talker has announced a crisis / final vote (<see cref="CommitVote"/>), the bot casts exactly that target even if a
    /// transfer or a prank changes its estimate before the ballot (an announced vote never silently changes).
    /// </summary>
    public sealed class LineBot
    {
        private readonly int seat;
        private readonly LineRandom rng;
        private readonly LineBotPolicy policy;
        private uint request;
        private int week = int.MinValue;
        private LinePhase phase;
        private double phaseStart = double.NaN;
        private double readyAt;
        private bool triedWhistle, triedInvestigation;
        /// <summary>Deals: punishments already sent this week (key: kind * 16 + target), when each task kind started (silence
        /// money counts from there), my group's revealed choices per task kind, and a sent return waiting to show in the view.</summary>
        private readonly HashSet<int> punished = new HashSet<int>();
        private readonly double[] kindStart = { double.NaN, double.NaN, double.NaN, double.NaN };
        private readonly int[][] groupChoices = { null, null, null, null };
        private double refundWaitUntil = double.NegativeInfinity;
        private int refundTo = -1;
        /// <summary>Returns of silence money this bot sent (target, amount, time): matched with the view's transfers.</summary>
        private readonly List<double[]> returned = new List<double[]>();
        /// <summary>Earliest time the bot wants to be asked again after a null (a return waiting for the transfer limit).</summary>
        private double wakeWanted = double.PositiveInfinity;
        /// <summary>Talk previews: the task choice / first ballot this bot sent in the phase that started at
        /// <see cref="decidedAt"/> (the view may not show it yet).</summary>
        private double decidedAt = double.NaN;
        private int decidedChoice = -1, decidedVote = -1;
        /// <summary>Players this bot saw take in a task (from its own group reveals) or exposed publicly.</summary>
        private readonly HashSet<int> culprits = new HashSet<int>();
        /// <summary>Richest votes: the bot's estimate of everyone's prim, and its fixed per-target noise salt.</summary>
        private readonly LineBotLedger ledger;
        private readonly uint salt;
        private LineConfig config;
        /// <summary>Announced votes (Richest): crisis = key -1, final round r = key r; for the week <see cref="commitWeek"/>.</summary>
        private readonly Dictionary<int, int> committed = new Dictionary<int, int>();
        private int commitWeek = int.MinValue;
        private const int CrisisKey = -1;

        /// <summary>Maximum seeded delay before acting in a phase (0 = act immediately).</summary>
        public double ThinkSeconds;
        /// <summary>Earliest host time the bot wants to be asked again (+∞ = nothing to do in this phase).</summary>
        public double WakeAt { get; private set; } = double.PositiveInfinity;
        public LineBotPolicy Policy { get { return policy; } }
        /// <summary>How this bot votes (default: the estimated richest rival).</summary>
        public LineBotVoteStyle VoteStyle { get; set; } = LineBotVoteStyle.Richest;
        /// <summary>How this bot punishes (default: the v5.1 silence deals).</summary>
        public LineBotPunishStyle PunishStyle { get; set; } = LineBotPunishStyle.Deals;
        /// <summary>Host time before which neither the bot nor its talker sends another transfer (the engine allows one per
        /// <see cref="LineConfig.TransferMinInterval"/>; a client's view lags, so both sides note what they sent here).</summary>
        public double TransferHoldUntil { get; private set; } = double.NegativeInfinity;

        /// <summary>The talker sent a transfer at <paramref name="now"/>: the bot waits before sending its own.</summary>
        public void NoteTransfer(double now, LineConfig config)
        {
            TransferHoldUntil = Math.Max(TransferHoldUntil, now + (config ?? LineConfig.CreateDefault()).TransferMinInterval + 0.3);
        }

        public LineBot(int seat, int seed, LineBotPolicy policy = LineBotPolicy.Honest)
        {
            if (seat < 0 || seat >= LineProtocol.MaxPlayers || (byte)policy > (byte)LineBotPolicy.Mixed) throw new ArgumentException("Invalid bot.");
            this.seat = seat;
            this.policy = policy;
            rng = new LineRandom(seed);
            ledger = new LineBotLedger(seat);
            salt = unchecked((uint)seed * 2654435761u);
        }

        private LineAction Action(LineView v, LineVerb verb, int target = -1, int a = -1)
        {
            return new LineAction(v.WeekId, ++request, verb, target, a);
        }

        private bool Greedy(LineRandom r)
        {
            if (policy == LineBotPolicy.Mixed) return r.Unit() < 0.5;
            return policy == LineBotPolicy.Greedy;
        }

        public LineAction Next(LineView v, LineConfig config, double now)
        {
            WakeAt = double.PositiveInfinity;
            if (v == null || v.Me != seat) return null;
            if (config != null) this.config = config;
            // learning what the view shows is not a decision (no random draw): every view feeds the estimate
            ledger.Ingest(v);
            if (v.Phase == LinePhase.Results || v.Phase == LinePhase.Abandoned || v.Phase == LinePhase.None) return null;
            var me = v.Players.Find(p => p.Id == seat);
            if (me == null || !me.Connected) return null;
            if (week != v.WeekId)
            {
                week = v.WeekId; culprits.Clear(); phaseStart = double.NaN;
                punished.Clear(); returned.Clear(); refundTo = -1; refundWaitUntil = TransferHoldUntil = double.NegativeInfinity;
                for (int i = 0; i < kindStart.Length; i++) { kindStart[i] = double.NaN; groupChoices[i] = null; }
            }
            if (v.Phase == LinePhase.TaskPlay && v.Task != null && (int)v.Task.Kind < kindStart.Length && double.IsNaN(kindStart[(int)v.Task.Kind]))
                kindStart[(int)v.Task.Kind] = v.PhaseStartTime;
            RememberGroup(v);
            if (v.Phase != phase || v.PhaseStartTime != phaseStart)
            {
                phase = v.Phase;
                phaseStart = v.PhaseStartTime;
                triedWhistle = triedInvestigation = false;
                decidedChoice = decidedVote = -1;
                decidedAt = double.NaN;
                double length = Math.Max(0, v.PhaseEndTime - v.PhaseStartTime);
                readyAt = v.PhaseStartTime + (ThinkSeconds > 0 ? Math.Min(rng.Unit() * ThinkSeconds, 0.8 * length) : 0);
                Learn(v, culprits);
            }
            if (now < readyAt) { WakeAt = readyAt; return null; }
            wakeWanted = double.PositiveInfinity;
            var action = Decide(v, config ?? LineConfig.CreateDefault(), now);
            WakeAt = action == null ? wakeWanted : now;
            return action;
        }

        private void Learn(LineView v, HashSet<int> into)
        {
            if (v.Task != null && v.Task.Revealed && v.Task.Kind != TaskKind.WeekendShift)
                foreach (var choice in v.Task.Choices)
                    if (choice.Player != seat && choice.Choice == LineChoice.Take) into.Add(choice.Player);
            if (v.Meeting != null)
                foreach (var line in v.Meeting.Dossier)
                {
                    if (line.Kind == DossierKind.CashCaught) into.Add(line.Player);
                    if (line.Kind == DossierKind.Whistle) into.Add(line.Other);
                    if (line.Kind == DossierKind.Investigation && line.N1 == 1) into.Add(line.Other);
                }
        }

        private LineAction Decide(LineView v, LineConfig config, double now)
        {
            var m = v.Mine;
            bool deals = PunishStyle == LineBotPunishStyle.Deals;
            switch (v.Phase)
            {
                case LinePhase.TaskPlay:
                    if (v.Task == null || v.Task.Submitted) break;
                    int choice = Choose(v, rng);
                    decidedChoice = choice;
                    decidedAt = phaseStart;
                    return Action(v, LineVerb.SubmitTask, a: choice);
                case LinePhase.Break:
                    if (deals)
                    {
                        var whistle = Punish(v, config, true);
                        if (whistle != null) return whistle;
                    }
                    else if (!triedWhistle && m.WitnessOf.Count > 0)
                    {
                        triedWhistle = true;
                        return Action(v, LineVerb.Whistle, m.WitnessOf[0]);
                    }
                    break;
                case LinePhase.MeetingDossier:
                case LinePhase.MeetingDiscussion:
                    Learn(v, culprits);
                    if (deals)
                    {
                        // (one decision per target and week, whichever of the two phases it falls in)
                        var investigation = Punish(v, config, false);
                        if (investigation != null) return investigation;
                        break;
                    }
                    // Legacy (v5.0): decided again in each of the two phases, kept as it was for LineSim --rules old
                    if (triedInvestigation || !v.Features.InvestigationOpen || m.Prim < config.InvestigationCost) return null;
                    triedInvestigation = true;
                    foreach (int target in m.InvestigationTargets)
                        if (culprits.Contains(target) && (policy == LineBotPolicy.Greedy || policy == LineBotPolicy.Mixed && Greedy(rng)))
                            return Action(v, LineVerb.Investigate, target);
                    return null;
                case LinePhase.MeetingVote:
                case LinePhase.CrisisVote:
                case LinePhase.FinalVote:
                    Learn(v, culprits);
                    for (int i = 0; i < m.Ballots.Count; i++)
                    {
                        if (m.Ballots[i] != -1) continue;
                        var options = VoteOptions(v, v.Phase == LinePhase.FinalVote);
                        if (options.Count == 0) break;
                        int target = VoteStyle == LineBotVoteStyle.MostBlame ? Suspect(v, options, rng, culprits)
                            : Richest(v, options, rng, Committed(v, v.Phase == LinePhase.FinalVote));
                        if (i == 0) { decidedVote = target; decidedAt = phaseStart; }
                        return Action(v, LineVerb.Vote, target, i);
                    }
                    break;
            }
            return deals ? ReturnSilenceMoney(v, config, now) : null;
        }

        // ---- punishments and silence deals (LineBotPunishStyle.Deals; docs/V5_BOT_TALK.md §4.1)

        private const int KindWhistle = 0, KindInvestigation = 1;

        /// <summary>The whistle (<paramref name="whistle"/>, Petty Cash break) or the investigation (meeting 1) this bot
        /// sends now, if any: the first eligible target it punishes (<see cref="WillPunish"/>). Greedy decides again as long
        /// as the window is open, so silence money that arrives later can still be betrayed.</summary>
        private LineAction Punish(LineView v, LineConfig config, bool whistle)
        {
            var m = v.Mine;
            List<int> targets;
            if (whistle) targets = m.WitnessOf;
            else
            {
                if (!v.Features.InvestigationOpen || m.InvestigationUsed || m.Prim < config.InvestigationCost) return null;
                targets = m.InvestigationTargets;
            }
            int kind = whistle ? KindWhistle : KindInvestigation;
            foreach (int target in targets)
            {
                if (punished.Contains(kind * 16 + target) || !WillPunish(v, whistle, target)) continue;
                punished.Add(kind * 16 + target);
                return Action(v, whistle ? LineVerb.Whistle : LineVerb.Investigate, target);
            }
            return null;
        }

        /// <summary>
        /// Honest (and Mixed when it plays Honest for that decision) never keeps silence money: whatever a colleague it
        /// punishes sent it since that task started goes back, until meeting 1 ends. One transfer at a time, waiting for the
        /// engine's limit and for the last return to show in the view.
        /// </summary>
        private LineAction ReturnSilenceMoney(LineView v, LineConfig config, double now)
        {
            if (v.Day > 0 || v.Phase < LinePhase.Intro || v.Phase > LinePhase.MeetingVerdict || !v.Features.TransfersOpen) return null;
            if (refundTo >= 0 && now < refundWaitUntil && Owed(v, refundTo) > 0) { wakeWanted = refundWaitUntil; return null; }
            refundTo = -1;
            foreach (var p in v.Players)
            {
                if (p.Id == seat) continue;
                int owed = Owed(v, p.Id);
                if (owed <= 0) continue;
                int amount = Math.Min(owed, Math.Min(config.TransferMax, v.Mine.Prim));
                if (amount <= 0) return null;
                double ready = Math.Max(v.Mine.NextTransferAt, TransferHoldUntil);
                if (now < ready) { wakeWanted = ready; return null; }
                refundTo = p.Id;
                refundWaitUntil = now + 3.0;
                returned.Add(new double[] { p.Id, amount, now });
                TransferHoldUntil = now + config.TransferMinInterval + 0.3;
                return Action(v, LineVerb.Transfer, p.Id, amount);
            }
            return null;
        }

        /// <summary>Silence money still to return to <paramref name="target"/>: what it sent me since the earliest task in
        /// which I punish it honestly, minus what I returned. 0 when I do not punish it honestly.</summary>
        public int Owed(LineView v, int target)
        {
            if (v == null) return 0;
            double since = double.PositiveInfinity;
            if (Victim(target) && HonestMode(v, false, target)) since = Math.Min(since, Since(TaskKind.ClientBonus));
            if (Witness(target) && HonestMode(v, true, target)) since = Math.Min(since, Since(TaskKind.PettyCash));
            if (double.IsPositiveInfinity(since)) return 0;
            int got = 0;
            foreach (var x in v.Mine.Transfers) if (x.Time >= since - 1e-9 && x.From == target && x.To == seat) got += x.Amount;
            return got - Returned(v, target);
        }

        /// <summary>What the view shows of my returns to <paramref name="target"/>: each return I sent matches the first unclaimed
        /// transfer of mine to it with that amount, at or after the moment I sent it (a client's clock may run a second
        /// early). A return the engine refused never shows, so it is sent again.</summary>
        private int Returned(LineView v, int target)
        {
            var used = new HashSet<int>();
            int total = 0;
            foreach (var r in returned)
            {
                if ((int)r[0] != target) continue;
                for (int i = 0; i < v.Mine.Transfers.Count; i++)
                {
                    var x = v.Mine.Transfers[i];
                    if (used.Contains(i) || x.From != seat || x.To != target || x.Amount != (int)r[1] || x.Time < r[2] - 1.0) continue;
                    used.Add(i);
                    total += x.Amount;
                    break;
                }
            }
            return total;
        }

        /// <summary>Remembers my group's revealed Client Bonus / Petty Cash choices (who took, and my own choice).</summary>
        private void RememberGroup(LineView v)
        {
            var t = v.Task;
            if (t == null || !t.Revealed || t.Kind == TaskKind.WeekendShift || (int)t.Kind >= groupChoices.Length || groupChoices[(int)t.Kind] != null) return;
            var choices = new int[LineProtocol.MaxPlayers];
            for (int i = 0; i < choices.Length; i++) choices[i] = -1;
            foreach (var c in t.Choices) if (c.Player >= 0 && c.Player < choices.Length) choices[c.Player] = c.Choice;
            groupChoices[(int)t.Kind] = choices;
        }

        private int ChoiceOf(TaskKind kind, int id)
        {
            var c = groupChoices[(int)kind];
            return c == null || id < 0 || id >= c.Length ? -1 : c[id];
        }

        /// <summary>I shared the Client Bonus and <paramref name="target"/> (my group) took: I may investigate it.</summary>
        public bool Victim(int target)
        {
            return target != seat && ChoiceOf(TaskKind.ClientBonus, seat) == LineChoice.Share && ChoiceOf(TaskKind.ClientBonus, target) == LineChoice.Take;
        }

        /// <summary>I left the Petty Cash and <paramref name="target"/> was the only taker of my group: I may whistle.</summary>
        public bool Witness(int target)
        {
            var c = groupChoices[(int)TaskKind.PettyCash];
            if (c == null || target == seat || c[seat] != LineChoice.Leave || target < 0 || target >= c.Length || c[target] != LineChoice.Take) return false;
            int takers = 0;
            foreach (int x in c) if (x == LineChoice.Take) takers++;
            return takers == 1;
        }

        private double Since(TaskKind kind)
        {
            double t = kindStart[(int)kind];
            return double.IsNaN(t) ? double.NegativeInfinity : t;
        }

        /// <summary>A seeded value in [0, 1) for a deal decision (seed, week, topic, kind, target): no random-stream draw.</summary>
        private double Coin(int weekId, string topic, int kind, int target)
        {
            unchecked
            {
                uint h = salt ^ (uint)(seat + 1) * 0x27D4EB2Du ^ (uint)weekId * 0x165667B1u ^ (uint)(kind + 3) * 0x9E3779B1u ^ (uint)(target + 11) * 0x85EBCA6Bu;
                foreach (char ch in topic) { h ^= ch; h *= 16777619u; }
                h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
                return h / 4294967296.0;
            }
        }

        /// <summary>
        /// Whether this bot treats punishing <paramref name="target"/> (<paramref name="whistle"/>: the Petty Cash whistle,
        /// else the meeting 1 investigation) as Honest does: punish, and send any silence money back. Honest always, Greedy
        /// never, Mixed on a seeded coin per decision. The coin is per colleague: in the rare match where the same colleague
        /// both took my Client Bonus share and stole the Petty Cash in front of me, one mode covers both decisions (its
        /// silence money cannot be told apart).
        /// </summary>
        public bool HonestMode(LineView v, bool whistle, int target)
        {
            if (policy == LineBotPolicy.Honest) return true;
            if (policy == LineBotPolicy.Greedy || v == null) return false;
            return Coin(v.WeekId, "mode", 0, target) < 0.5;
        }

        /// <summary>Greedy mode: once paid for its silence, whether the bot still punishes (a seeded 50% coin per decision).</summary>
        public bool Betrays(LineView v, bool whistle, int target)
        {
            return v != null && Coin(v.WeekId, "betray", whistle ? KindWhistle : KindInvestigation, target) < 0.5;
        }

        /// <summary>Prim <paramref name="target"/> has sent this bot since the task of that punishment started (Client Bonus
        /// for the investigation, Petty Cash for the whistle): its silence money.</summary>
        public int SilenceMoney(LineView v, bool whistle, int target)
        {
            if (v == null) return 0;
            double since = Since(whistle ? TaskKind.PettyCash : TaskKind.ClientBonus);
            int got = 0;
            foreach (var x in v.Mine.Transfers) if (x.From == target && x.To == seat && x.Time >= since - 1e-9) got += x.Amount;
            return got;
        }

        /// <summary>
        /// Whether this bot punishes <paramref name="target"/> when it can (deals): Honest mode always; Greedy mode only once
        /// paid (<see cref="SilenceMoney"/> &gt; 0, or <paramref name="assumePaid"/> for a deal still being offered) and then
        /// on its betrayal coin. A preview: the same answer the bot acts on, without changing anything.
        /// </summary>
        public bool WillPunish(LineView v, bool whistle, int target, bool assumePaid = false)
        {
            if (HonestMode(v, whistle, target)) return true;
            return (assumePaid || SilenceMoney(v, whistle, target) > 0) && Betrays(v, whistle, target);
        }

        private List<int> VoteOptions(LineView v, bool final)
        {
            var options = new List<int>();
            if (final) { if (v.Final != null) options.AddRange(v.Final.Options); }
            else foreach (var p in v.Players) if (!p.Fired) options.Add(p.Id);
            options.Remove(seat);
            return options;
        }

        private int Choose(LineView v, LineRandom r)
        {
            bool greedy = Greedy(r);
            switch (v.Task.Kind)
            {
                case TaskKind.ClientBonus:
                case TaskKind.PettyCash:
                    return greedy ? LineChoice.Take : LineChoice.Share;
                case TaskKind.WeekendShift:
                    if (greedy) return LineChoice.Skip;
                    // Simple count: the cleanest Needed + 1 connected players (suç, then seat) volunteer.
                    var order = v.Players.FindAll(p => p.Connected);
                    order.Sort((x, y) => x.Blame != y.Blame ? x.Blame.CompareTo(y.Blame) : x.Id.CompareTo(y.Id));
                    int rank = order.FindIndex(p => p.Id == seat);
                    return rank >= 0 && rank <= v.Task.Needed ? LineChoice.Volunteer : LineChoice.Skip;
                default:
                    return 0;
            }
        }

        /// <summary>A known culprit first, then the highest suç; ties broken by the bot's seeded random.</summary>
        private int Suspect(LineView v, List<int> options, LineRandom r, HashSet<int> suspects)
        {
            var known = options.FindAll(id => suspects.Contains(id));
            var pool = known.Count > 0 ? known : options;
            int best = int.MinValue;
            foreach (int id in pool) best = Math.Max(best, BlameOf(v, id));
            var top = pool.FindAll(id => BlameOf(v, id) == best);
            return top[r.Index(top.Count)];
        }

        /// <summary>
        /// The rival with the highest estimated prim (plus this bot's fixed noise for that rival); ties broken by
        /// <paramref name="r"/> (one draw, like <see cref="Suspect"/>). An announced target (<paramref name="announced"/>)
        /// that is still an option wins; the draw happens anyway so the random stream does not depend on announcements.
        /// </summary>
        private int Richest(LineView v, List<int> options, LineRandom r, int announced)
        {
            var est = ledger.Estimate(config);
            double best = double.NegativeInfinity;
            var top = new List<int>();
            foreach (int id in options)
            {
                double e = Worth(est, id);
                if (e > best + 1e-9) { best = e; top.Clear(); top.Add(id); }
                else if (Math.Abs(e - best) <= 1e-9) top.Add(id);
            }
            int pick = top[r.Index(top.Count)];
            return announced >= 0 && options.Contains(announced) ? announced : pick;
        }

        /// <summary>The estimate of <paramref name="id"/> as this bot weighs it: the ledger plus a fixed per-(bot, colleague)
        /// noise in ±0.4 prim (derived from the bot's seed), so bots with the same facts disagree a little.</summary>
        private double Worth(double[] est, int id)
        {
            return (id >= 0 && id < est.Length ? est[id] : 0) + Noise(id);
        }

        private double Noise(int id)
        {
            unchecked
            {
                uint h = (uint)(seat * 73856093) ^ (uint)(id * 19349663) ^ salt;
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return ((h % 1000) / 1000.0 - 0.5) * 0.8;
            }
        }

        /// <summary>The announced target for the vote of <paramref name="v"/> (crisis or final round), -1 when none.</summary>
        private int Committed(LineView v, bool final)
        {
            if (VoteStyle != LineBotVoteStyle.Richest || commitWeek != v.WeekId) return -1;
            int key;
            if (!CommitKey(v, final, out key)) return -1;
            int target;
            return committed.TryGetValue(key, out target) ? target : -1;
        }

        private static bool CommitKey(LineView v, bool final, out int key)
        {
            key = CrisisKey;
            if (final) { key = v.Final != null ? Math.Max(0, v.Final.Round) : 0; return true; }
            return v.Phase == LinePhase.CrisisCalls || v.Phase == LinePhase.CrisisVote;
        }

        // ---- talk support (read-only previews; docs/V5_BOT_TALK.md)

        /// <summary>True when <see cref="Next"/> has already seen the phase of <paramref name="v"/> (its think delay is drawn).</summary>
        private bool Seen(LineView v)
        {
            return week == v.WeekId && phase == v.Phase && phaseStart == v.PhaseStartTime;
        }

        /// <summary>
        /// The task choice (<see cref="LineChoice"/>) this bot submits in the current TaskPlay, or submitted, without
        /// changing anything: the same policy call on a copy of the bot's random state. Outside TaskPlay: the view's choice.
        /// </summary>
        public int PlannedTaskChoice(LineView v)
        {
            if (v == null || v.Task == null) return 0;
            if (v.Phase != LinePhase.TaskPlay) return v.Task.MyChoice;
            if (Seen(v) && decidedAt == v.PhaseStartTime && decidedChoice >= 0) return decidedChoice;
            if (v.Task.Submitted) return v.Task.MyChoice;
            var r = rng.Copy();
            if (!Seen(v) && ThinkSeconds > 0) r.Unit(); // the think delay is drawn first when the bot meets the phase
            return Choose(v, r);
        }

        /// <summary>
        /// The colleague this bot names on its crisis ballot (<paramref name="final"/> false) or final ballot (true), without
        /// changing anything. <paramref name="phasesAhead"/> = phase starts still to come before that vote (0 in the vote
        /// itself, 1 from CrisisCalls for the crisis vote). <paramref name="options"/> overrides the candidates (a final vote
        /// seen from before it starts); null = the view's. Returns -1 when there is nobody to vote for.
        /// </summary>
        public int PlannedVote(LineView v, bool final, int phasesAhead = 0, List<int> options = null)
        {
            if (v == null) return -1;
            bool inVote = phasesAhead == 0 && (v.Phase == LinePhase.CrisisVote || v.Phase == LinePhase.FinalVote);
            if (inVote && Seen(v) && decidedAt == v.PhaseStartTime && decidedVote >= 0) return decidedVote;
            if (inVote && v.Mine.Ballots.Count > 0 && v.Mine.Ballots[0] >= 0) return v.Mine.Ballots[0];
            var list = options != null ? new List<int>(options) : VoteOptions(v, final);
            list.Remove(seat);
            if (list.Count == 0) return -1;
            var r = rng.Copy();
            int draws = phasesAhead + (Seen(v) ? 0 : 1);
            if (ThinkSeconds > 0) for (int i = 0; i < draws; i++) r.Unit();
            if (VoteStyle == LineBotVoteStyle.MostBlame)
            {
                var known = week == v.WeekId ? new HashSet<int>(culprits) : new HashSet<int>();
                Learn(v, known);
                return Suspect(v, list, r, known);
            }
            ledger.Ingest(v);
            return Richest(v, list, r, Committed(v, final));
        }

        /// <summary>
        /// Richest votes: the talker has just announced (a Vote / NotVote claim) that this bot's crisis ballot
        /// (<paramref name="final"/> false, from CrisisCalls or CrisisVote) or final ballot of the current round goes to
        /// <paramref name="target"/> (the <see cref="PlannedVote"/> it built the line from). From then on the bot casts
        /// that target (in a revote too, while it is one of the tied) whatever new facts arrive, so the announcement stays
        /// true. The first announcement of a vote wins. MostBlame bots ignore it (their behaviour is unchanged).
        /// </summary>
        public void CommitVote(LineView v, bool final, int target)
        {
            if (v == null || target < 0 || target == seat || VoteStyle != LineBotVoteStyle.Richest) return;
            int key;
            if (!CommitKey(v, final, out key)) return;
            if (commitWeek != v.WeekId) { commitWeek = v.WeekId; committed.Clear(); }
            if (!committed.ContainsKey(key)) committed[key] = target;
        }

        /// <summary>
        /// This bot's current estimate of everyone's prim (seated players; fired = 0, its own = exact), from its own views
        /// only. Learns from <paramref name="v"/> (when it is this bot's view) but decides nothing.
        /// </summary>
        public Dictionary<int, double> EstimatedPrim(LineView v)
        {
            ledger.Ingest(v);
            var est = ledger.Estimate(config);
            var map = new Dictionary<int, double>();
            if (v != null) foreach (var p in v.Players) if (p.Id >= 0 && p.Id < est.Length) map[p.Id] = est[p.Id];
            return map;
        }

        /// <summary>
        /// The colleague this bot believes is the richest (its estimate plus its fixed noise, the same weighing as its
        /// votes; exact ties go to the lowest seat) among <paramref name="options"/> (null = every colleague still employed).
        /// With <paramref name="countMe"/> the bot's own exact balance competes too (the result is then its own seat when it
        /// believes it is the richest; it wins ties). What its talk says it thinks; not a vote (no random draw, no
        /// commitment). -1 when there is nobody.
        /// </summary>
        public int RichestRival(LineView v, List<int> options = null, bool countMe = false)
        {
            if (v == null) return -1;
            var list = options != null ? new List<int>(options) : VoteOptions(v, false);
            list.Remove(seat);
            if (list.Count == 0) return -1;
            list.Sort();
            ledger.Ingest(v);
            var est = ledger.Estimate(config);
            int best = -1;
            double bestWorth = double.NegativeInfinity;
            if (countMe && seat < est.Length) { best = seat; bestWorth = est[seat]; }
            foreach (int id in list)
            {
                double e = Worth(est, id);
                if (e > bestWorth + 1e-9) { bestWorth = e; best = id; }
            }
            return best;
        }

        private static int BlameOf(LineView v, int id)
        {
            var p = v.Players.Find(x => x.Id == id);
            return p == null ? 0 : p.Blame;
        }
    }
}
