using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    public enum LineBotPolicy : byte
    {
        /// <summary>Share, leave, volunteer when needed, whistle on a witnessed theft, vote for the most suspicious.</summary>
        Honest = 0,
        /// <summary>Take, take, skip; whistle when witness; investigate a partner who took; vote for the most suspicious.</summary>
        Greedy = 1,
        /// <summary>Honest or greedy, seeded per decision.</summary>
        Mixed = 2,
    }

    /// <summary>
    /// A view-only synthetic player (spec §13). It never calls, texts or uses the recorder. <see cref="Next"/> returns at
    /// most one action per call; call it until it returns null. With <see cref="ThinkSeconds"/> &gt; 0 the bot waits a
    /// seeded delay after each phase starts (see <see cref="WakeAt"/>).
    /// Talk support (docs/V5_BOT_TALK.md): <see cref="PlannedTaskChoice"/> and <see cref="PlannedVote"/> preview a decision
    /// before the bot makes it by running the same policy on a copy of its random state, so they never change what it does.
    /// Calls, chat lines and payments are <see cref="LineBotTalker"/>'s job.
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
        /// <summary>Talk previews: the task choice / first ballot this bot sent in the phase that started at
        /// <see cref="decidedAt"/> (the view may not show it yet).</summary>
        private double decidedAt = double.NaN;
        private int decidedChoice = -1, decidedVote = -1;
        /// <summary>Players this bot saw take in a task (from its own group reveals) or exposed publicly.</summary>
        private readonly HashSet<int> culprits = new HashSet<int>();

        /// <summary>Maximum seeded delay before acting in a phase (0 = act immediately).</summary>
        public double ThinkSeconds;
        /// <summary>Earliest host time the bot wants to be asked again (+∞ = nothing to do in this phase).</summary>
        public double WakeAt { get; private set; } = double.PositiveInfinity;
        public LineBotPolicy Policy { get { return policy; } }

        public LineBot(int seat, int seed, LineBotPolicy policy = LineBotPolicy.Honest)
        {
            if (seat < 0 || seat >= LineProtocol.MaxPlayers || (byte)policy > (byte)LineBotPolicy.Mixed) throw new ArgumentException("Invalid bot.");
            this.seat = seat;
            this.policy = policy;
            rng = new LineRandom(seed);
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
            if (v == null || v.Me != seat || v.Phase == LinePhase.Results || v.Phase == LinePhase.Abandoned || v.Phase == LinePhase.None) return null;
            var me = v.Players.Find(p => p.Id == seat);
            if (me == null || !me.Connected) return null;
            if (week != v.WeekId) { week = v.WeekId; culprits.Clear(); phaseStart = double.NaN; }
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
            var action = Decide(v, config ?? LineConfig.CreateDefault());
            WakeAt = action == null ? double.PositiveInfinity : now;
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

        private LineAction Decide(LineView v, LineConfig config)
        {
            var m = v.Mine;
            switch (v.Phase)
            {
                case LinePhase.TaskPlay:
                    if (v.Task == null || v.Task.Submitted) return null;
                    int choice = Choose(v, rng);
                    decidedChoice = choice;
                    decidedAt = phaseStart;
                    return Action(v, LineVerb.SubmitTask, a: choice);
                case LinePhase.Break:
                    if (!triedWhistle && m.WitnessOf.Count > 0)
                    {
                        triedWhistle = true;
                        return Action(v, LineVerb.Whistle, m.WitnessOf[0]);
                    }
                    return null;
                case LinePhase.MeetingDossier:
                case LinePhase.MeetingDiscussion:
                    Learn(v, culprits);
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
                        if (options.Count == 0) return null;
                        int target = Suspect(v, options, rng, culprits);
                        if (i == 0) { decidedVote = target; decidedAt = phaseStart; }
                        return Action(v, LineVerb.Vote, target, i);
                    }
                    return null;
                default:
                    return null;
            }
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
            var known = week == v.WeekId ? new HashSet<int>(culprits) : new HashSet<int>();
            Learn(v, known);
            return Suspect(v, list, r, known);
        }

        private static int BlameOf(LineView v, int id)
        {
            var p = v.Players.Find(x => x.Id == id);
            return p == null ? 0 : p.Blame;
        }
    }
}
