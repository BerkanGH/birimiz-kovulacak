using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// A bot's mouth (docs/V5_BOT_TALK.md): phone calls, answers to a human's question buttons, meeting chat lines and the
    /// prim it promised. Pure C#, one per bot seat, fed the bot's own <see cref="LineView"/> (nothing else it could not
    /// know) and its <see cref="LineBot"/> (whose decisions it previews but never changes).
    /// Truth: every line is picked by comparing it with the bot's real choice. Honest never says anything false; Greedy
    /// claims the cooperative choice and makes promises it does not keep; Mixed lies on about half of the topics where it
    /// has something to hide (a seeded coin per topic) and keeps about half of its promises.
    /// Random-looking decisions (whether to call, delays, coins) are hashes of the seed and a topic, so they do not depend
    /// on network timing.
    /// Votes: with <see cref="LineBotVoteStyle.Richest"/> (the default) the bots explain their votes by money (the rival they
    /// estimate to be the richest), voice that suspicion in the meeting chat, and every announced crisis / final vote is
    /// committed (<see cref="LineBot.CommitVote"/>) so it cannot silently change before the ballot. MostBlame bots keep the
    /// suç-board lines.
    /// Silence deals (v5.1): a Greedy / Mixed bot that took (a Client Bonus betrayal, a sole Petty Cash theft) pays every
    /// victim / witness 1 prim for silence in the break (a call, else a text); a bot that is paid by the colleague it could
    /// punish answers with what its <see cref="LineBot"/> will really do (<see cref="LineBot.WillPunish"/>): Honest sends
    /// the money back (the bot's own transfer) and punishes, Greedy keeps it and may still punish.
    /// </summary>
    public sealed partial class LineBotTalker
    {
        public readonly int Seat;
        public readonly LineBotPolicy Policy;
        readonly LineBot bot;
        readonly ulong seed;

        /// <summary>-calltest owns the calls of Intro and CrisisCalls: no planned calls there (answers and lines still run).</summary>
        public bool QuietCallPhases;
        /// <summary>Seconds of the ring tone; the bot answers after 1-3 rings.</summary>
        public double RingSeconds = 1.45;

        uint request;
        int week = int.MinValue;
        LinePhase phase = LinePhase.None;
        double phaseStart = double.NaN;
        double scale = 1, k = 1;
        LineConfig config = LineConfig.CreateDefault();

        // ------------------------------------------------------------------ memory (only what this seat saw)

        sealed class TaskMemo
        {
            public TaskKind Kind;
            public int Number;
            public readonly List<int> Group = new List<int>();
            /// <summary>Revealed choices of my group (Weekend Shift: everyone's, from the public roster).</summary>
            public readonly Dictionary<int, int> Choices = new Dictionary<int, int>();
            public bool Revealed;
            public int Needed = -1;
            public int Mine = -1;
            public bool Failed;
            /// <summary>When its TaskPlay started (silence money counts from there).</summary>
            public double Start = double.NaN;
        }

        readonly TaskMemo[] tasks = new TaskMemo[3];
        int transfersSeen;
        readonly Dictionary<int, int> paidBy = new Dictionary<int, int>();

        // ------------------------------------------------------------------ phone

        sealed class Due
        {
            public double At;
            public BotLine Line;
            public bool Ask;
            public BotQuestionSet AskSet;
            public int AskIndex;
            public int AnswerTo = -1;
            public BotQuestionSet AnswerSet;
            public int AnswerIndex;
            public bool Thanks;
            /// <summary>Built when it is posted (a line that names what the bot thinks right then), instead of <see cref="Line"/>.</summary>
            public Func<LineView, BotLine> Make;
        }

        sealed class Talk
        {
            public int CallId, Other;
            public bool Outgoing, OtherIsBot;
            public string Reason = "";
            public double Start, EndAt, HangAt = -1;
            public bool HungUp;
            public readonly List<Due> Queue = new List<Due>();
        }

        sealed class PlannedCall
        {
            public int Target;
            public double At;
            public string Reason;
            public int Tries;
            public bool Done;
        }

        Talk talk;
        readonly List<PlannedCall> planned = new List<PlannedCall>();
        string dialReason = "", lastReason = "";
        int dialTarget = -1;
        double ringKey = double.NaN, answerAt, refuseAt, declineAt;
        bool refuse, refuseSaid, answered;
        int ringCount, callCount;
        readonly List<BotAsk> heard = new List<BotAsk>();

        // ------------------------------------------------------------------ promises

        enum Cond { Now, IfITook, IfListenerVolunteers }

        sealed class Promise
        {
            public int To, Amount, Task = -1;
            public Cond When;
            public bool Keep, Done;
            public string Key;
            public double NotBefore;
        }

        readonly List<Promise> promises = new List<Promise>();

        // ------------------------------------------------------------------ silence deals (docs/V5_BOT_TALK.md §4.1)

        /// <summary>Silence money I owe a victim / witness of mine: said on the phone or, past the deadline, by text.</summary>
        sealed class Hush
        {
            public int Target, Task;
            public TaskKind Kind;
            public double Deadline, At = -1;
            public bool Done;
            public string Via = "";
        }

        readonly List<Hush> hushes = new List<Hush>();
        /// <summary>Deal reactions already said (key: target * 2 + whistle).</summary>
        readonly HashSet<int> dealReacted = new HashSet<int>();
        /// <summary>When meeting 1 ended (silence money is counted until then).</summary>
        double dealsEnd = double.NaN;
        /// <summary>Every payment this talker sent (to, amount, time, silence money?): at Results they are told apart from the
        /// bot's own returns of silence money, and silence money never counts as keeping another promise.</summary>
        sealed class Sent { public int To, Amount; public double Time; public bool Hush; }
        readonly List<Sent> sent = new List<Sent>();

        // ------------------------------------------------------------------ meeting chat

        int segment = int.MinValue, chatCount;
        /// <summary>The engine's message / transfer rate limits as this talker last caused them (a client's view lags).</summary>
        double nextChatAt, nextPayAt;
        readonly List<Due> chat = new List<Due>();
        long msgSeen;
        bool accusedAnswered, situationPlanned, defended, wealthPlanned;
        int lastAccuser = -1;
        double voteKey = double.NaN;
        readonly HashSet<int> leakReacted = new HashSet<int>();
        readonly HashSet<string> chatSaid = new HashSet<string>();
        readonly Dictionary<int, int> chatsBy = new Dictionary<int, int>();

        // ------------------------------------------------------------------ votes (the log at Results)

        sealed class VoteStart
        {
            public BallotKind Kind;
            public int Round;
            public bool Revote;
            public double Time;
            public readonly List<int> Options = new List<int>();
        }

        readonly List<VoteStart> voteStarts = new List<VoteStart>();

        // ------------------------------------------------------------------ logs and checks

        readonly List<string> pendingLog = new List<string>();
        readonly List<BotLine> claims = new List<BotLine>();
        bool flushed;
        int lines, lies, calls, answeredCalls, refusedCalls, answers, chats, paid, broken;

        public LineBotTalker(int seat, int seed, LineBotPolicy policy, LineBot bot)
        {
            if (seat < 0 || seat >= LineProtocol.MaxPlayers) throw new ArgumentException("Invalid seat.");
            Seat = seat;
            Policy = policy;
            this.bot = bot;
            this.seed = unchecked((ulong)(long)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL);
        }

        /// <summary>Highest message seq this talker has read (the host passes only newer messages).</summary>
        public long MessagesSeen => msgSeen;

        // ================================================================== inputs

        /// <summary>A question from the other party of my call (a human's button 1/2/3, or another bot).</summary>
        public void Hear(BotAsk ask)
        {
            if (ask.To != Seat || ask.From == Seat || heard.Count > 8) return;
            heard.Add(ask);
        }

        // ================================================================== tick

        /// <summary>
        /// One step (call it a few times a second with the bot's fresh view, after <see cref="LineBot.Next"/>):
        /// <paramref name="messages"/> are this seat's messages (meeting chat, texts) for spotting accusations.
        /// </summary>
        public void Tick(LineView v, LineConfig c, double now, IReadOnlyList<LineMessage> messages, BotTalkOutput o)
        {
            if (v == null || o == null || v.Me != Seat) return;
            if (c != null) config = c;
            if (v.WeekId != week) Reset(v.WeekId);
            if (v.Phase == LinePhase.Results || v.Phase == LinePhase.Abandoned)
            {
                if (!flushed) Finish(v, o);
                // Yaramazlıklar: the cake (and reactions to it) run on through the results
                if (v.Phase == LinePhase.Results) Pranks(v, now, o);
                return;
            }
            var me = P(v, Seat);
            if (me == null || !me.Connected || v.Phase == LinePhase.None) return;
            scale = Math.Max(0.05, Math.Min(3.0, config.Durations.TaskPlay / 60.0));
            k = Math.Max(0.4, Math.Min(1.0, scale));
            if (v.Phase != phase || v.PhaseStartTime != phaseStart) OnPhase(v, now);
            Remember(v, now, o);
            Phone(v, now, o);
            HushByText(v, now);
            Payments(v, now, o);
            Meeting(v, now, messages, o);
            Pranks(v, now, o);
        }

        void Reset(int newWeek)
        {
            week = newWeek;
            phase = LinePhase.None;
            phaseStart = double.NaN;
            for (int i = 0; i < tasks.Length; i++) tasks[i] = null;
            transfersSeen = 0;
            nextChatAt = nextPayAt = 0;
            paidBy.Clear();
            talk = null;
            planned.Clear();
            ringKey = double.NaN;
            heard.Clear();
            promises.Clear();
            hushes.Clear();
            sent.Clear();
            dealReacted.Clear();
            dealsEnd = double.NaN;
            segment = int.MinValue;
            chat.Clear();
            msgSeen = 0;
            leakReacted.Clear();
            chatSaid.Clear();
            chatsBy.Clear();
            pendingLog.Clear();
            claims.Clear();
            voteStarts.Clear();
            flushed = false;
            lines = lies = calls = answeredCalls = refusedCalls = answers = chats = paid = broken = 0;
            ringCount = callCount = 0;
            ResetPranks();
        }

        // ================================================================== helpers

        static LinePlayerView P(LineView v, int id)
        {
            foreach (var p in v.Players) if (p.Id == id) return p;
            return null;
        }

        static bool Busy(LineView v, int id)
        {
            foreach (var call in v.LineBoard) if (call.Caller == id || call.Callee == id) return true;
            return false;
        }

        static bool Available(LineView v, int id)
        {
            var p = P(v, id);
            return p != null && p.Connected && !Busy(v, id);
        }

        /// <summary>A seeded value in [0, 1) for a topic (independent of the order topics come up in).</summary>
        double U(string topic)
        {
            ulong h = seed ^ (0xC2B2AE3D27D4EB4FUL * (ulong)(Seat + 1)) ^ (ulong)(uint)week * 0x165667B19E3779F9UL;
            foreach (char ch in topic) { h ^= ch; h *= 0x100000001B3UL; }
            unchecked
            {
                h += 0x9E3779B97F4A7C15UL;
                h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
                h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
                h ^= h >> 31;
            }
            return (h >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>Mixed: lie on this topic (one coin per topic, so it does not flip-flop within a task).</summary>
        bool MixedLies(string topic) { return U("lie:" + topic) < 0.5; }

        /// <summary>Mixed keeps about half of its promises.</summary>
        bool MixedKeeps(string topic) { return U("keep:" + topic) < 0.5; }

        string Key(string stem) { return stem + LineBotLines.Suffix(Policy); }

        BotLine Line(string key, int listener, params int[] args)
        {
            var l = new BotLine { Key = key, Speaker = Seat, Listener = listener, Policy = Policy };
            if (args != null) l.Args.AddRange(args);
            return l;
        }

        static BotLine Claim(BotLine l, BotClaimKind kind, int value, bool truthful, int task = -1)
        {
            l.Claim = kind;
            l.ClaimValue = value;
            l.ClaimTask = task;
            l.Truthful = truthful;
            return l;
        }

        LineAction Act(LineView v, LineVerb verb, int target = LineIds.None, int a = -1, string text = "")
        {
            return new LineAction(v.WeekId, ++request, verb, target, a, -1, -1, text);
        }

        void Log(string s) { pendingLog.Add("bottalk " + s); }

        static string Yes(bool b) { return b ? "yes" : "no"; }

        /// <summary>Counts and logs a line that was really delivered.</summary>
        void Said(BotLine l, double now, BotTalkOutput o)
        {
            l.Time = now;
            o.Said.Add(l);
            lines++;
            if (!l.Truthful) lies++;
            if (l.Claim != BotClaimKind.None) claims.Add(l);
            Log(string.Format(CultureInfo.InvariantCulture, "line seat={0} policy={1} key={2} to={3} truthful={4} claim={5}:{6}:{7} via={8} t={9:0.0}",
                Seat, Policy, l, l.Listener, Yes(l.Truthful), l.Claim, l.ClaimTask, l.ClaimValue, l.Shout ? "shout" : l.Text ? (l.Listener < 0 ? "chat" : "text") : "phone", now));
        }

        TaskMemo Memo(int number) { return number >= 0 && number < tasks.Length ? tasks[number] : null; }

        /// <summary>When the phone closes: a call made while choosing runs on through the reveal and the break.</summary>
        double CallsEnd(LineView v)
        {
            var d = config.Durations;
            double end = v.PhaseEndTime;
            bool more = v.TaskNumber >= 0 && v.TaskNumber < 2;
            switch (v.Phase)
            {
                case LinePhase.Intro: return end + d.TaskPlay;
                case LinePhase.TaskPlay: return end + d.TaskReveal + (more ? d.Break : 0);
                case LinePhase.TaskReveal:
                    if (more) return end + d.Break;
                    // after the last task the crisis calls follow unless the shift came up short (meeting 2)
                    return v.Task != null && v.Task.Revealed && v.Task.Kind == TaskKind.WeekendShift && v.Task.Roster.Count >= v.Task.Needed ? end + d.CrisisCalls : end;
                case LinePhase.Break: return end + (v.TaskNumber == 0 ? d.TaskPlay : 0);
                default: return end;
            }
        }

        TaskMemo MemoOf(TaskKind kind)
        {
            foreach (var t in tasks) if (t != null && t.Kind == kind) return t;
            return null;
        }

        /// <summary>My choice in a task: the bot's plan while choosing, else what was revealed.</summary>
        int MyChoice(LineView v, TaskMemo t)
        {
            if (t == null) return -1;
            if (v.Phase == LinePhase.TaskPlay && v.Task != null && v.Task.Number == t.Number && bot != null) return bot.PlannedTaskChoice(v);
            if (t.Mine >= 0) return t.Mine;
            return v.Task != null && v.Task.Number == t.Number ? v.Task.MyChoice : -1;
        }

        bool Took(LineView v, TaskKind kind)
        {
            var t = MemoOf(kind);
            return t != null && t.Revealed && MyChoice(v, t) == LineChoice.Take;
        }

        /// <summary>Colleagues I know took (Client Bonus / Petty Cash in my group, caught or whistled publicly).</summary>
        HashSet<int> KnownCulprits(LineView v)
        {
            var set = new HashSet<int>();
            foreach (var t in tasks)
            {
                if (t == null || !t.Revealed || t.Kind == TaskKind.WeekendShift) continue;
                foreach (var kv in t.Choices) if (kv.Key != Seat && kv.Value == LineChoice.Take) set.Add(kv.Key);
            }
            if (v.Meeting != null)
                foreach (var d in v.Meeting.Dossier)
                {
                    if (d.Kind == DossierKind.CashCaught && d.Player != Seat) set.Add(d.Player);
                    if (d.Kind == DossierKind.Whistle && d.Other != Seat) set.Add(d.Other);
                    if (d.Kind == DossierKind.Investigation && d.N1 == 1 && d.Other != Seat) set.Add(d.Other);
                }
            return set;
        }

        // ================================================================== memory

        void Remember(LineView v, double now, BotTalkOutput o)
        {
            var t = v.Task;
            if (t != null && t.Number >= 0 && t.Number < tasks.Length)
            {
                var m = tasks[t.Number];
                if (m == null)
                {
                    m = tasks[t.Number] = new TaskMemo { Kind = t.Kind, Number = t.Number, Needed = t.Needed };
                    m.Group.AddRange(t.Group.Count > 0 ? t.Group : v.Mine.Group);
                    if (v.Phase == LinePhase.TaskPlay) m.Start = v.PhaseStartTime;
                }
                if (t.Revealed && !m.Revealed)
                {
                    m.Revealed = true;
                    m.Mine = t.MyChoice;
                    foreach (var ch in t.Choices) m.Choices[ch.Player] = ch.Choice;
                    if (t.Kind == TaskKind.WeekendShift)
                    {
                        foreach (var p in v.Players) m.Choices[p.Id] = t.Roster.Contains(p.Id) ? LineChoice.Volunteer : LineChoice.Skip;
                        m.Failed = t.Roster.Count < t.Needed;
                    }
                    else if (t.Kind == TaskKind.ClientBonus) m.Failed = t.Takers > 0;
                }
            }
            if (double.IsNaN(dealsEnd) && v.Day >= 1) dealsEnd = now;
            // prim that came in: thank the sender (on the phone if we are talking, else a short call or a text); silence
            // money from a colleague I could punish gets the answer my LineBot will act on instead
            var list = v.Mine.Transfers;
            for (int i = transfersSeen; i < list.Count; i++)
            {
                var tr = list[i];
                if (tr.To != Seat || tr.From == Seat) continue;
                int had;
                paidBy.TryGetValue(tr.From, out had);
                paidBy[tr.From] = had + tr.Amount;
                Log(string.Format(CultureInfo.InvariantCulture, "received seat={0} from={1} amount={2}", Seat, tr.From, tr.Amount));
                bool whistle;
                if (DealOpen(v, tr.From, out whistle))
                {
                    if (dealReacted.Add(tr.From * 2 + (whistle ? 1 : 0))) DealReaction(v, tr.From, whistle, now);
                    continue;
                }
                if (talk != null && talk.Other == tr.From && !talk.HungUp)
                {
                    talk.Queue.Add(new Due { At = now + 0.8 * k, Thanks = true });
                    if (talk.EndAt < double.MaxValue) talk.EndAt = Math.Max(talk.EndAt, now + 4 * k);
                }
                else if (v.Features.CallsOpen) Plan(tr.From, now + 1.5 * k, "thanks");
                else if (v.Features.TextsOpen)
                {
                    var l = Line(Key("bot.thanks"), tr.From);
                    l.Text = true;
                    chat.Add(new Due { At = now + 1.5, Line = l });
                }
            }
            transfersSeen = list.Count;
        }

        // ================================================================== phases: who to call

        void OnPhase(LineView v, double now)
        {
            phase = v.Phase;
            phaseStart = v.PhaseStartTime;
            planned.Clear();
            NoteVote(v);
            double len = Math.Max(1, v.PhaseEndTime - v.PhaseStartTime);
            string topic = "w" + week + "p" + (int)phase + "t" + v.TaskNumber + "d" + v.Day;
            if (QuietCallPhases && (phase == LinePhase.Intro || phase == LinePhase.CrisisCalls)) return;
            switch (phase)
            {
                case LinePhase.TaskPlay:
                {
                    if (v.Task == null) break;
                    double p = Policy == LineBotPolicy.Honest ? 0.6 : Policy == LineBotPolicy.Greedy ? 0.7 : 0.5;
                    if (v.Task.Kind == TaskKind.WeekendShift) p *= 0.6;
                    if (U("call:" + topic) >= p) break;
                    var who = new List<int>();
                    if (v.Task.Family == TaskFamily.Pair) { foreach (int id in v.Mine.Group) if (id != Seat) who.Add(id); }
                    else foreach (var pl in v.Players) if (pl.Id != Seat && pl.Connected) who.Add(pl.Id);
                    if (who.Count == 0) break;
                    int target = who[(int)(U("who:" + topic) * who.Count) % who.Count];
                    Plan(target, phaseStart + (1.0 + Seat * 0.6 + U("at:" + topic) * 2.0) * k, "task");
                    break;
                }
                case LinePhase.Break:
                {
                    int target;
                    string reason = BreakCall(v, out target);
                    if (reason != null) Plan(target, phaseStart + (2.5 + Seat * 1.0 + U("at:" + topic) * 3.0) * scale, reason);
                    // silence money: a Greedy / Mixed taker calls each victim / witness early in the break (a Petty Cash
                    // witness can whistle until the break ends); past the deadline it texts instead
                    var t = v.Task != null ? Memo(v.Task.Number) : null;
                    int i = 0;
                    foreach (int id in HushTargets(t))
                    {
                        if (hushes.Exists(h => h.Task == t.Number && h.Target == id)) continue;
                        hushes.Add(new Hush { Target = id, Task = t.Number, Kind = t.Kind, Deadline = phaseStart + len * (t.Kind == TaskKind.PettyCash ? 0.55 : 0.85) });
                        Plan(id, phaseStart + (0.6 + 0.4 * Seat + 2.0 * i++ + U("hush:" + topic + id) * 1.0) * scale, "hush");
                    }
                    break;
                }
                case LinePhase.TaskReveal:
                    // Weekend Shift came up short: call someone who did not sign (if there is time for a call)
                    if (v.Task != null && v.Task.Kind == TaskKind.WeekendShift && v.Task.Revealed && v.Task.Roster.Count < v.Task.Needed && len >= 6
                        && U("call:" + topic) < 0.5)
                    {
                        var who = new List<int>();
                        foreach (var pl in v.Players) if (pl.Id != Seat && pl.Connected && !v.Task.Roster.Contains(pl.Id)) who.Add(pl.Id);
                        if (who.Count == 0) foreach (var pl in v.Players) if (pl.Id != Seat && pl.Connected) who.Add(pl.Id);
                        if (who.Count > 0) Plan(who[(int)(U("who:" + topic) * who.Count) % who.Count], phaseStart + 0.6 + Seat * 0.3, "short");
                    }
                    break;
                case LinePhase.CrisisCalls:
                {
                    // bargaining: one or two colleagues, never the one I will vote for
                    int target = bot != null ? bot.PlannedVote(v, false, 1) : -1;
                    var who = new List<int>();
                    foreach (var pl in v.Players) if (pl.Id != Seat && pl.Connected && !pl.Fired && pl.Id != target) who.Add(pl.Id);
                    if (who.Count == 0) break;
                    double first = Policy == LineBotPolicy.Mixed ? 0.6 : 0.8, second = Policy == LineBotPolicy.Mixed ? 0.3 : 0.5;
                    int a = who[(int)(U("who1:" + topic) * who.Count) % who.Count];
                    if (U("call1:" + topic) < first) Plan(a, phaseStart + len * (0.08 + 0.04 * Seat + 0.08 * U("at1:" + topic)), "crisis");
                    if (who.Count > 1 && U("call2:" + topic) < second)
                    {
                        who.Remove(a);
                        int b = who[(int)(U("who2:" + topic) * who.Count) % who.Count];
                        Plan(b, phaseStart + len * (0.45 + 0.03 * Seat + 0.1 * U("at2:" + topic)), "crisis");
                    }
                    break;
                }
            }
        }

        /// <summary>A vote starts: remember when and among whom (for the vote log at Results).</summary>
        void NoteVote(LineView v)
        {
            if (v.Phase != LinePhase.MeetingVote && v.Phase != LinePhase.CrisisVote && v.Phase != LinePhase.FinalVote) return;
            var s = new VoteStart { Time = v.PhaseStartTime };
            if (v.Phase == LinePhase.FinalVote)
            {
                if (v.Final == null) return;
                s.Kind = BallotKind.Final;
                s.Round = v.Final.Round;
                s.Revote = v.Final.Revote;
                s.Options.AddRange(v.Final.Options);
            }
            else
            {
                s.Kind = v.Phase == LinePhase.MeetingVote ? BallotKind.Meeting : BallotKind.Crisis;
                s.Round = v.Phase == LinePhase.MeetingVote && v.Meeting != null ? v.Meeting.Index : 0;
                foreach (var p in v.Players) if (!p.Fired) s.Options.Add(p.Id);
            }
            s.Options.Remove(Seat);
            voteStarts.Add(s);
        }

        void Plan(int target, double at, string reason)
        {
            if (target < 0 || target == Seat) return;
            foreach (var p in planned) if (!p.Done && p.Target == target) { p.At = Math.Min(p.At, at); return; }
            planned.Add(new PlannedCall { Target = target, At = at, Reason = reason });
        }

        /// <summary>The break call this bot wants to make (victim → taker, witness → thief); a taker's silence money is a
        /// separate "hush" call (<see cref="HushTargets"/>).</summary>
        string BreakCall(LineView v, out int target)
        {
            target = -1;
            var t = v.Task != null ? Memo(v.Task.Number) : null;
            if (t == null || !t.Revealed) return null;
            int mine = t.Mine;
            if (t.Kind == TaskKind.ClientBonus)
            {
                foreach (int id in t.Group)
                {
                    if (id == Seat || !t.Choices.ContainsKey(id) || t.Choices[id] != LineChoice.Take) continue;
                    if (mine == LineChoice.Share || Policy == LineBotPolicy.Greedy) { target = id; return "victim"; }
                }
            }
            else if (t.Kind == TaskKind.PettyCash)
            {
                int thief = SoleTaker(t);
                if (thief >= 0 && thief != Seat && mine == LineChoice.Leave) { target = thief; return "witness"; }
            }
            return null;
        }

        /// <summary>Who a Greedy / Mixed bot pays for silence after a revealed task: the Client Bonus group-mates who shared
        /// while it took, or every witness of its sole Petty Cash theft. Honest never takes.</summary>
        List<int> HushTargets(TaskMemo t)
        {
            var list = new List<int>();
            if (t == null || !t.Revealed || Policy == LineBotPolicy.Honest || t.Mine != LineChoice.Take) return list;
            if (t.Kind == TaskKind.ClientBonus)
            {
                foreach (int id in t.Group)
                {
                    int c;
                    if (id != Seat && t.Choices.TryGetValue(id, out c) && c == LineChoice.Share) list.Add(id);
                }
            }
            else if (t.Kind == TaskKind.PettyCash && SoleTaker(t) == Seat)
                foreach (int id in t.Group) if (id != Seat) list.Add(id);
            return list;
        }

        /// <summary>The silence money line (a payment made on the spot: Greedy and Mixed both send it, it buys the deal).</summary>
        BotLine HushLine(TaskKind kind, int task, int other)
        {
            string key = kind == TaskKind.ClientBonus ? (Policy == LineBotPolicy.Greedy ? "bot.cb.hush.g" : "bot.cb.thief.m") : Key("bot.cash.thief");
            var l = Claim(Line(key, other), BotClaimKind.Pay, 1, true, task);
            l.ClaimRound = (int)Cond.Now;
            return l;
        }

        Hush PendingHush(int task, int other)
        {
            foreach (var h in hushes) if (h.Task == task && h.Target == other && !h.Done) return h;
            return null;
        }

        /// <summary>The hush call did not happen in time (busy, declined, missed): the line goes by text, the prim with it.</summary>
        void HushByText(LineView v, double now)
        {
            foreach (var h in hushes)
            {
                if (h.Done || now < h.Deadline) continue;
                if (talk != null && talk.Other == h.Target && !talk.HungUp) continue;
                var ph = v.Mine.Phone;
                if (ph.Other == h.Target && ph.Status != CallStatus.Idle) continue;
                var pl = P(v, h.Target);
                h.Done = true;
                if (pl == null || !pl.Connected || !v.Features.TextsOpen) { h.Via = "none"; continue; }
                var l = HushLine(h.Kind, h.Task, h.Target);
                l.Text = true;
                chat.Add(new Due { At = now, Line = l });
            }
        }

        static int SoleTaker(TaskMemo t)
        {
            int thief = -1, n = 0;
            foreach (var kv in t.Choices) if (kv.Value == LineChoice.Take) { thief = kv.Key; n++; }
            return n == 1 ? thief : -1;
        }

        // ================================================================== phone

        void Phone(LineView v, double now, BotTalkOutput o)
        {
            var ph = v.Mine.Phone;
            if (ph.Status == CallStatus.Ringing)
            {
                double key = ph.Since * 8 + ph.Other;
                if (key != ringKey) { ringKey = key; DecideRing(v, ph, now); }
                if (refuse)
                {
                    if (!refuseSaid && now >= refuseAt)
                    {
                        refuseSaid = true;
                        var l = Line(Key("bot.refuse"), ph.Other);
                        if (Policy == LineBotPolicy.Mixed)
                        {
                            bool keep = MixedKeeps("callback" + ringCount);
                            Claim(l, BotClaimKind.Callback, ph.Other, keep);
                            if (keep) Plan(ph.Other, now + 6 * k, "callback");
                        }
                        Deliver(l, now, o);
                        declineAt = now + 0.9;
                    }
                    else if (refuseSaid && now >= declineAt && declineAt > 0)
                    {
                        declineAt = -1;
                        o.Actions.Add(Act(v, LineVerb.Decline));
                    }
                }
                else if (!answered && now >= answerAt)
                {
                    answered = true;
                    answeredCalls++;
                    Log(string.Format(CultureInfo.InvariantCulture, "answer seat={0} from={1} t={2:0.0}", Seat, ph.Other, now));
                    o.Actions.Add(Act(v, LineVerb.Answer));
                }
                return;
            }
            if (ph.Status == CallStatus.Connected)
            {
                if (talk == null || talk.CallId != ph.CallId) StartTalk(v, ph, now);
                RunTalk(v, now, o);
                return;
            }
            if (talk != null)
            {
                Log(string.Format(CultureInfo.InvariantCulture, "callend seat={0} other={1} seconds={2:0.0}", Seat, talk.Other, now - talk.Start));
                talk = null;
            }
            heard.Clear();
            if (ph.Status == CallStatus.Dialing) return;
            PlaceCalls(v, now, o);
        }

        void DecideRing(LineView v, LinePhoneView ph, double now)
        {
            ringCount++;
            refuse = refuseSaid = answered = false;
            declineAt = 0;
            int caller = ph.Other;
            string topic = "ring" + ringCount;
            int rings = 1 + (int)(U("rings:" + topic) * 3);
            double left = Math.Max(0.5, ph.RingUntil - now);
            answerAt = now + Math.Min(rings * RingSeconds, left * 0.7);
            if (Policy == LineBotPolicy.Mixed && !MustTalk(v, caller) && U("refuse:" + topic) < 0.3) refuse = true;
            if (refuse)
            {
                refusedCalls++;
                refuseAt = now + Math.Min(RingSeconds, left * 0.5);
                Log(string.Format(CultureInfo.InvariantCulture, "refuse seat={0} from={1} t={2:0.0}", Seat, caller, now));
            }
        }

        /// <summary>Mixed never dodges a call it has something to say in (the break's victim / witness / thief business).</summary>
        bool MustTalk(LineView v, int other) { return BreakLineFor(v, other, false) != null; }

        void PlaceCalls(LineView v, double now, BotTalkOutput o)
        {
            if (!v.Features.CallsOpen || planned.Count == 0) return;
            if (QuietCallPhases && (v.Phase == LinePhase.Intro || v.Phase == LinePhase.CrisisCalls)) return;
            double left = CallsEnd(v) - now;
            if (left < Math.Max(3.0, 9.0 * scale)) return;
            foreach (var p in planned)
            {
                if (p.Done || now < p.At) continue;
                var pl = P(v, p.Target);
                if (pl == null || !pl.Connected) { p.Done = true; continue; }
                if (Busy(v, p.Target))
                {
                    p.At = now + 3.0 * k;
                    if (++p.Tries > 3) p.Done = true;
                    continue;
                }
                p.Done = true;
                dialReason = p.Reason;
                dialTarget = p.Target;
                calls++;
                callCount++;
                Log(string.Format(CultureInfo.InvariantCulture, "call seat={0} to={1} reason={2} t={3:0.0}", Seat, p.Target, p.Reason, now));
                o.Actions.Add(Act(v, LineVerb.Call, p.Target));
                return;
            }
        }

        void StartTalk(LineView v, LinePhoneView ph, double now)
        {
            bool outgoing = false;
            foreach (var c in v.Mine.CallLog) if (c.CallId == ph.CallId) outgoing = c.Outgoing;
            var other = P(v, ph.Other);
            talk = new Talk
            {
                CallId = ph.CallId, Other = ph.Other, Outgoing = outgoing, Start = now, OtherIsBot = other != null && other.IsBot,
                Reason = outgoing && dialTarget == ph.Other ? dialReason : outgoing ? "call" : "in",
            };
            lastReason = talk.Reason;
            foreach (var p in planned) if (p.Target == ph.Other) p.Done = true;
            string topic = "talk" + week + ":" + ph.CallId;
            double len = (outgoing ? 15 + 9 * U("len:" + topic) : 24 + 8 * U("len:" + topic)) * Math.Max(0.3, Math.Min(1.0, scale));
            if (talk.Reason == "thanks" || talk.Reason == "short") len = Math.Min(len, 7 * k);
            talk.EndAt = Math.Min(now + len, CallsEnd(v) - 0.8);
            // -calltest hangs up its own calls (it records one): this talker only talks there
            if (QuietCallPhases && (v.Phase == LinePhase.Intro || v.Phase == LinePhase.CrisisCalls)) talk.EndAt = double.MaxValue;
            if (!outgoing)
            {
                talk.Queue.Add(new Due { At = now + 0.6 * k, Line = Greeting(ph.Other) });
                var situation = BreakLineFor(v, ph.Other, true);
                if (situation != null) talk.Queue.Add(new Due { At = now + 2.4 * k, Line = situation });
            }
            else
            {
                var opener = Opener(v, talk.Reason, ph.Other);
                if (opener != null) talk.Queue.Add(new Due { At = now + 0.8 * k, Line = opener });
            }
            {
                // each side asks one of the context's questions and the other answers (a -bot player is not flagged as a
                // bot by the engine, so this is sent to anyone: a person's client ignores it)
                var set = Askable(v);
                int n = LineBotLines.Questions(set).Count;
                if (n > 0)
                    talk.Queue.Add(new Due { At = now + (outgoing ? 3.2 : 5.6) * k, Ask = true, AskSet = set, AskIndex = (int)(U("ask:" + topic + outgoing) * n) % n });
            }
            Log(string.Format(CultureInfo.InvariantCulture, "connected seat={0} other={1} outgoing={2} reason={3} t={4:0.0}", Seat, ph.Other, Yes(outgoing), talk.Reason, now));
        }

        void RunTalk(LineView v, double now, BotTalkOutput o)
        {
            var t = talk;
            if (t.HungUp) return;
            // questions heard (resolved with the current view)
            foreach (var a in heard)
            {
                if (a.From != t.Other) continue;
                t.Queue.Add(new Due { At = now + (0.9 + 0.5 * U("think:" + t.CallId + ":" + t.Queue.Count)) * k, AnswerTo = a.From, AnswerSet = a.Set, AnswerIndex = a.Index });
                if (t.EndAt < double.MaxValue) t.EndAt = Math.Max(t.EndAt, Math.Min(now + 10 * k, CallsEnd(v) - 0.8));
            }
            heard.Clear();
            t.Queue.Sort((x, y) => x.At.CompareTo(y.At));
            while (t.Queue.Count > 0 && t.Queue[0].At <= now)
            {
                var d = t.Queue[0];
                t.Queue.RemoveAt(0);
                if (d.Ask)
                {
                    o.Asks.Add(new BotAsk { From = Seat, To = t.Other, Set = d.AskSet, Index = d.AskIndex });
                    Log(string.Format(CultureInfo.InvariantCulture, "ask seat={0} to={1} set={2} index={3}", Seat, t.Other, d.AskSet, d.AskIndex));
                    continue;
                }
                BotLine line = d.Line;
                if (d.Thanks) line = Line(Key("bot.thanks"), t.Other);
                if (d.AnswerTo >= 0)
                {
                    line = Answer(v, d.AnswerTo, d.AnswerSet, d.AnswerIndex);
                    if (line != null) answers++;
                }
                if (line == null) continue;
                line.Listener = t.Other;
                Deliver(line, now, o);
                Promised(v, line, now);
            }
            if (t.HangAt < 0 && now >= t.EndAt && CallsEnd(v) - now > 1.0)
            {
                Deliver(Line(Key("bot.bye"), t.Other), now, o);
                t.HangAt = now + Math.Max(0.8, 1.4 * k);
            }
            else if (t.HangAt >= 0 && now >= t.HangAt)
            {
                t.HungUp = true;
                o.Actions.Add(Act(v, LineVerb.HangUp));
            }
        }

        void Deliver(BotLine line, double now, BotTalkOutput o)
        {
            if (!LineBotLines.Valid(line)) { Log("invalid seat=" + Seat + " key=" + line); return; }
            Said(line, now, o);
            o.Lines.Add(line);
        }

        BotLine Greeting(int caller)
        {
            return Policy == LineBotPolicy.Honest ? Line("bot.answer.h", caller, caller) : Line(Key("bot.answer"), caller);
        }

        /// <summary>The first line of a call I placed.</summary>
        BotLine Opener(LineView v, string reason, int other)
        {
            switch (reason)
            {
                case "task": return v.Phase == LinePhase.TaskPlay || v.Phase == LinePhase.TaskReveal ? TaskOpener(v, other) : BreakLineFor(v, other, false);
                case "victim":
                case "witness": return BreakLineFor(v, other, false);
                case "hush":
                {
                    var t = v.Task != null ? Memo(v.Task.Number) : null;
                    if (t != null && PendingHush(t.Number, other) != null) return HushLine(t.Kind, t.Number, other);
                    foreach (var h in hushes) if (!h.Done && h.Target == other) return HushLine(h.Kind, h.Task, other);
                    return BreakLineFor(v, other, false);
                }
                case "short":
                {
                    var t = MemoOf(TaskKind.WeekendShift);
                    int mine = t != null ? t.Mine : -1;
                    var l = Line(Key("bot.shift.short"), other);
                    if (Policy == LineBotPolicy.Greedy) Claim(l, BotClaimKind.Choice, LineChoice.Volunteer, mine == LineChoice.Volunteer, t != null ? t.Number : -1);
                    return l;
                }
                case "crisis": return CrisisOpener(v, other);
                case "thanks": return Line(Key("bot.thanks"), other);
                case "call":
                case "callback":
                    if (v.Phase == LinePhase.TaskPlay) return TaskOpener(v, other);
                    if (v.Phase == LinePhase.CrisisCalls) return CrisisOpener(v, other);
                    return BreakLineFor(v, other, false);
                default: return null;
            }
        }

        BotLine TaskOpener(LineView v, int other)
        {
            var t = v.Task;
            // (a call placed while choosing may connect just after the reveal: the claims then use the revealed choice)
            if (t == null || (v.Phase != LinePhase.TaskPlay && v.Phase != LinePhase.TaskReveal)) return null;
            int choice = bot != null ? bot.PlannedTaskChoice(v) : t.MyChoice;
            string topic = "task" + t.Number;
            switch (t.Kind)
            {
                case TaskKind.ClientBonus:
                    if (Policy == LineBotPolicy.Honest) return Claim(Line("bot.cb.call.h", other, other), BotClaimKind.Choice, LineChoice.Share, choice == LineChoice.Share, t.Number);
                    if (Policy == LineBotPolicy.Mixed && choice == LineChoice.Take && !MixedLies(topic)) return null;
                    return Claim(Line(Key("bot.cb.call"), other), BotClaimKind.Choice, LineChoice.Share, choice == LineChoice.Share, t.Number);
                case TaskKind.PettyCash:
                    if (Policy == LineBotPolicy.Mixed) return Line("bot.cash.call.m", other);
                    return Claim(Line(Key("bot.cash.call"), other), BotClaimKind.Choice, LineChoice.Leave, choice == LineChoice.Leave, t.Number);
                case TaskKind.WeekendShift:
                    if (Policy == LineBotPolicy.Mixed) return Line("bot.shift.call.m", other);
                    if (Policy == LineBotPolicy.Greedy) return Claim(Line("bot.shift.call.g", other), BotClaimKind.Choice, LineChoice.Volunteer, choice == LineChoice.Volunteer, t.Number);
                    if (choice == LineChoice.Volunteer) return Claim(Line("bot.shift.call.h", other, Math.Max(1, t.Needed)), BotClaimKind.Choice, LineChoice.Volunteer, true, t.Number);
                    // Honest skips when enough cleaner colleagues will sign (LineBot's simple count): it says so and pays a
                    // colleague who signs instead
                    var l = Claim(Line("bot.shift.call.h_skip", other, Math.Max(1, t.Needed)), BotClaimKind.Choice, LineChoice.Skip, true, t.Number);
                    return l;
            }
            return null;
        }

        BotLine CrisisOpener(LineView v, int other)
        {
            if (v.Phase != LinePhase.CrisisCalls && v.Phase != LinePhase.CrisisVote) return null;
            int ahead = v.Phase == LinePhase.CrisisCalls ? 1 : 0;
            int target = bot != null ? bot.PlannedVote(v, false, ahead) : -1;
            if (Policy == LineBotPolicy.Mixed || target < 0) return Line("bot.crisis.call.m", other);
            if (Rich)
            {
                if (Policy == LineBotPolicy.Honest)
                    return Announce(v, false, target, Vote(Line(Richest(v, target, false) ? "bot.crisis.call.h_rich" : "bot.crisis.call.h_win", other, target), target, true, BallotKind.Crisis, 0));
                int named = target != other ? target : Another(v, other, target, false);
                if (named < 0) return Line("bot.crisis.call.m", other);
                return Announce(v, false, target, Vote(Line("bot.crisis.call.g_rich", other, named), named, named == target, BallotKind.Crisis, 0));
            }
            if (Policy == LineBotPolicy.Honest)
                return Vote(Line(Leads(v, target) ? "bot.crisis.call.h" : "bot.crisis.call.h_known", other, target), target, true, BallotKind.Crisis, 0);
            // Greedy names its real target (it votes like everyone); never the listener
            int name = target != other ? target : Another(v, other, target, false);
            if (name < 0) return Line("bot.crisis.call.m", other);
            return Vote(Line("bot.crisis.call.g", other, name), name, name == target, BallotKind.Crisis, 0);
        }

        /// <summary>The bot votes for the estimated richest rival (<see cref="LineBotVoteStyle.Richest"/>).</summary>
        bool Rich => bot != null && bot.VoteStyle == LineBotVoteStyle.Richest;

        /// <summary>An announced vote (a Vote / NotVote claim): the bot casts <paramref name="target"/> (the vote the line was
        /// built from) whatever arrives before the ballot, so the line can never turn into an accidental lie.</summary>
        BotLine Announce(LineView v, bool final, int target, BotLine l)
        {
            if (bot != null) bot.CommitVote(v, final, target);
            return l;
        }

        /// <summary>
        /// Whether this bot believes <paramref name="target"/> is the richest of everyone the line speaks about: the other
        /// colleagues (<paramref name="final"/>: the final candidates) and the bot itself when it is one of them (its own
        /// balance is exact). Honest only says "the richest" when it believes it; otherwise it says it wants to win.
        /// </summary>
        bool Richest(LineView v, int target, bool final)
        {
            if (bot == null || target < 0) return false;
            List<int> pool = null;
            if (final && v.Final != null)
            {
                pool = new List<int>();
                foreach (int id in v.Final.Candidates) { var p = P(v, id); if (p != null && !p.Fired) pool.Add(id); }
            }
            bool meToo = pool == null || pool.Contains(Seat);
            return bot.RichestRival(v, pool, meToo) == target;
        }

        static BotLine Vote(BotLine l, int target, bool truthful, BallotKind ballot, int round)
        {
            Claim(l, BotClaimKind.Vote, target, truthful);
            l.ClaimBallot = ballot;
            l.ClaimRound = round;
            return l;
        }

        /// <summary>The target has the highest suç among the colleagues one could vote for (ties count as leading).</summary>
        bool Leads(LineView v, int target)
        {
            int best = int.MinValue, mine = int.MinValue;
            foreach (var p in v.Players)
            {
                if (p.Id == Seat || p.Fired) continue;
                if (v.Final != null && v.Final.Options.Count > 0 && (v.Phase == LinePhase.FinalVote) && !v.Final.Options.Contains(p.Id)) continue;
                best = Math.Max(best, p.Blame);
                if (p.Id == target) mine = p.Blame;
            }
            return mine != int.MinValue && mine == best;
        }

        /// <summary>Someone else to name (not me, not <paramref name="avoid"/>, not <paramref name="target"/>), or -1.</summary>
        int Another(LineView v, int avoid, int target, bool final)
        {
            var list = new List<int>();
            if (final && v.Final != null) list.AddRange(v.Final.Options);
            else foreach (var p in v.Players) if (!p.Fired) list.Add(p.Id);
            list.RemoveAll(id => id == Seat || id == avoid || id == target);
            if (list.Count == 0) return -1;
            list.Sort();
            return list[(int)(U("another:" + avoid + ":" + target + ":" + (int)v.Phase) * list.Count) % list.Count];
        }

        /// <summary>
        /// The break business with <paramref name="other"/>: victim / witness lines (to the thief), thief lines (Petty Cash:
        /// to the witness; Client Bonus: the answer to the victim's call). <paramref name="callee"/>: I am answering.
        /// </summary>
        BotLine BreakLineFor(LineView v, int other, bool callee)
        {
            if (v.Phase != LinePhase.Break || v.Task == null) return null;
            var t = Memo(v.Task.Number);
            if (t == null || !t.Revealed || !t.Group.Contains(other)) return null;
            int mine = t.Mine, theirs;
            if (!t.Choices.TryGetValue(other, out theirs)) return null;
            if (t.Kind == TaskKind.ClientBonus)
            {
                if (theirs == LineChoice.Take && (mine == LineChoice.Share || Policy == LineBotPolicy.Greedy))
                    return Line(Key("bot.cb.victim"), other);
                // the victim calls me first: the silence money right away (once)
                if (callee && mine == LineChoice.Take && theirs == LineChoice.Share && PendingHush(t.Number, other) != null) return HushLine(t.Kind, t.Number, other);
                return null;
            }
            if (t.Kind == TaskKind.PettyCash)
            {
                int thief = SoleTaker(t);
                if (thief == other && mine == LineChoice.Leave)
                {
                    if (Policy == LineBotPolicy.Honest) return Claim(Line("bot.cash.witness.h", other), BotClaimKind.Whistle, 1, true);
                    // "pay me and I won't press": true when, once paid, it keeps quiet (no betrayal coin)
                    if (Policy == LineBotPolicy.Greedy) return Claim(Line("bot.cash.witness.g", other), BotClaimKind.Silence, 1, !PunishesPaid(v, true, other));
                    return Line("bot.cash.witness.m", other);
                }
                if (thief == Seat && PendingHush(t.Number, other) != null) return HushLine(t.Kind, t.Number, other);
            }
            return null;
        }

        /// <summary>A line that promises prim: Honest keeps it, Greedy never does, Mixed about half the time.</summary>
        BotLine PayLine(BotLine l, int to, int amount, Cond when, int task, string topic)
        {
            bool keep = Policy == LineBotPolicy.Honest || (Policy == LineBotPolicy.Mixed && MixedKeeps(topic));
            Claim(l, BotClaimKind.Pay, amount, keep, task);
            l.ClaimRound = (int)when;
            return l;
        }

        /// <summary>Registers the promise a delivered line made (paid in <see cref="Payments"/>).</summary>
        void Promised(LineView v, BotLine l, double now)
        {
            if (IsHush(l.Key))
                foreach (var h in hushes)
                    if (h.Task == l.ClaimTask && h.Target == l.Listener && h.At < 0) { h.Done = true; h.At = now; h.Via = l.Text ? "text" : "phone"; }
            if (l.Claim == BotClaimKind.Pay)
            {
                var p = new Promise { To = l.Listener, Amount = l.ClaimValue, When = (Cond)l.ClaimRound, Task = l.ClaimTask, Keep = l.Truthful, Key = l.Key, NotBefore = now + 1.0 * k };
                promises.Add(p);
                Log(string.Format(CultureInfo.InvariantCulture, "promise seat={0} to={1} amount={2} when={3} keep={4}", Seat, p.To, p.Amount, p.When, Yes(p.Keep)));
                if (!p.Keep) broken++;
            }
            else if (l.Key == "bot.shift.call.h_skip")
            {
                // "sign up and I'll send you 1 prim" (Honest: kept when the colleague signs)
                var t = v.Task;
                var p = new Promise { To = l.Listener, Amount = 1, When = Cond.IfListenerVolunteers, Task = t != null ? t.Number : 2, Keep = true, Key = l.Key, NotBefore = now };
                promises.Add(p);
                Log(string.Format(CultureInfo.InvariantCulture, "promise seat={0} to={1} amount=1 when={2} keep=yes", Seat, p.To, p.When));
            }
        }

        static bool IsHush(string key)
        {
            return key == "bot.cb.hush.g" || key == "bot.cb.thief.m" || key == "bot.cash.thief.g" || key == "bot.cash.thief.m";
        }

        // ================================================================== silence deals: the bot that could punish

        /// <summary>
        /// Whether prim from <paramref name="from"/> is silence money for a punishment this bot can still decide:
        /// the Petty Cash whistle (I witnessed its sole theft; the reveal or the break of that task) or the investigation
        /// (it took my Client Bonus share; until meeting 1's investigation window closes).
        /// </summary>
        bool DealOpen(LineView v, int from, out bool whistle)
        {
            whistle = false;
            if (bot == null || bot.PunishStyle != LineBotPunishStyle.Deals || v.Day > 0) return false;
            var pc = MemoOf(TaskKind.PettyCash);
            if (bot.Witness(from) && pc != null && v.TaskNumber == pc.Number && (v.Phase == LinePhase.TaskReveal || v.Phase == LinePhase.Break))
            {
                whistle = true;
                return true;
            }
            return bot.Victim(from) && (v.Phase <= LinePhase.MeetingDiscussion);
        }

        /// <summary>Greedy mode: whether, once paid, the bot still punishes (its betrayal coin); Honest mode: always.</summary>
        bool PunishesPaid(LineView v, bool whistle, int target)
        {
            return bot == null || bot.WillPunish(v, whistle, target, true);
        }

        /// <summary>The answer to silence money: Honest mode sends it back and punishes (the LineBot returns it), Greedy mode
        /// keeps it and promises silence (a lie when its betrayal coin says punish anyway).</summary>
        void DealReaction(LineView v, int from, bool whistle, double now)
        {
            bool honest = bot.HonestMode(v, whistle, from);
            BotLine l;
            if (honest) l = Claim(Line(Policy == LineBotPolicy.Mixed ? "bot.deal.refund.m" : "bot.deal.refund.h", from), BotClaimKind.Whistle, 1, true);
            else l = Claim(Line(Policy == LineBotPolicy.Mixed ? "bot.deal.keep.m" : "bot.deal.keep.g", from), BotClaimKind.Silence, 1, !bot.Betrays(v, whistle, from));
            Log(string.Format(CultureInfo.InvariantCulture, "deal seat={0} policy={1} from={2} kind={3} mode={4} t={5:0.0}",
                Seat, Policy, from, whistle ? "whistle" : "investigation", honest ? "honest" : "greedy", now));
            if (talk != null && talk.Other == from && !talk.HungUp)
            {
                talk.Queue.Add(new Due { At = now + 0.8 * k, Line = l });
                if (talk.EndAt < double.MaxValue) talk.EndAt = Math.Max(talk.EndAt, now + 4 * k);
            }
            else if (v.Features.TextsOpen)
            {
                l.Text = true;
                chat.Add(new Due { At = now + 1.2, Line = l });
            }
        }

        /// <summary>What can still be asked: the human buttons' context, plus the task just revealed (an answer to a question
        /// asked while choosing may come a moment after the reveal).</summary>
        static BotQuestionSet Askable(LineView v)
        {
            var set = LineBotLines.QuestionSetOf(v);
            if (set != BotQuestionSet.None || v.Task == null) return set;
            if (v.Phase == LinePhase.TaskReveal)
                return v.Task.Kind == TaskKind.ClientBonus ? BotQuestionSet.ClientBonus : v.Task.Kind == TaskKind.PettyCash ? BotQuestionSet.PettyCash
                    : v.Task.Kind == TaskKind.WeekendShift ? BotQuestionSet.WeekendShift : BotQuestionSet.None;
            return set;
        }

        /// <summary>The answer to question <paramref name="index"/> of <paramref name="set"/> (null when it no longer fits).</summary>
        BotLine Answer(LineView v, int asker, BotQuestionSet set, int index)
        {
            if (set == BotQuestionSet.None || set != Askable(v)) return null;
            if (set == BotQuestionSet.Crisis) return CrisisAnswer(v, asker, index);
            var t = v.Task;
            int choice = bot != null ? bot.PlannedTaskChoice(v) : t.MyChoice;
            string topic = "task" + t.Number;
            bool lie = Policy == LineBotPolicy.Mixed && MixedLies(topic);
            int n = t.Number;
            switch (set)
            {
                case BotQuestionSet.ClientBonus:
                {
                    bool share = choice == LineChoice.Share;
                    if (index == 0)
                    {
                        if (Policy == LineBotPolicy.Mixed && !share && !lie) return Line("bot.cb.a1.m_dodge", asker);
                        return Claim(Line(Key("bot.cb.a1"), asker), BotClaimKind.Choice, LineChoice.Share, share, n);
                    }
                    if (index == 1)
                    {
                        if (Policy == LineBotPolicy.Mixed && !share && !lie) return Line("bot.cb.a3.m", asker);
                        return Claim(Line(Key("bot.cb.a2"), asker), BotClaimKind.Choice, LineChoice.Share, share, n);
                    }
                    if (Policy == LineBotPolicy.Greedy) return Claim(Line("bot.cb.a3.g", asker), BotClaimKind.Choice, LineChoice.Share, share, n);
                    return Line(Key("bot.cb.a3"), asker);
                }
                case BotQuestionSet.PettyCash:
                {
                    bool leave = choice == LineChoice.Leave;
                    if (index == 0)
                    {
                        if (Policy == LineBotPolicy.Mixed) return Line("bot.cash.a1.m", asker);
                        return Claim(Line(Key("bot.cash.a1"), asker), BotClaimKind.Choice, LineChoice.Leave, leave, n);
                    }
                    if (index == 1)
                    {
                        // "if I take it, will you keep quiet?": Honest whistles (and sends silence money back); Greedy names a
                        // price, true when, once paid, it keeps quiet (its betrayal coin for this asker); Mixed names a price
                        // in its Greedy mode, in its Honest mode it lies (coin) or dodges
                        if (Policy == LineBotPolicy.Honest) return Claim(Line("bot.cash.a2.h", asker), BotClaimKind.Whistle, 1, true);
                        bool honestMode = bot != null && bot.HonestMode(v, true, asker);
                        if (Policy == LineBotPolicy.Mixed && honestMode && !lie) return Line("bot.cash.witness.m", asker);
                        return Claim(Line(Key("bot.cash.a2"), asker), BotClaimKind.Silence, 1, !PunishesPaid(v, true, asker));
                    }
                    if (Policy == LineBotPolicy.Honest) return Claim(Line("bot.cash.a3.h", asker), BotClaimKind.Choice, LineChoice.Leave, leave, n);
                    // "if I take it, 1 prim": paid after the reveal when the bot took (Greedy never pays)
                    return PayLine(Line(Key("bot.cash.a3"), asker), asker, 1, Cond.IfITook, n, "cashq3" + asker);
                }
                case BotQuestionSet.WeekendShift:
                {
                    bool sign = choice == LineChoice.Volunteer;
                    if (index == 0)
                    {
                        if (Policy == LineBotPolicy.Honest)
                            return sign ? Claim(Line("bot.shift.a1.h", asker), BotClaimKind.Choice, LineChoice.Volunteer, true, n)
                                : Claim(Line("bot.shift.a1.h_skip", asker), BotClaimKind.Choice, LineChoice.Skip, true, n);
                        if (Policy == LineBotPolicy.Mixed && !sign && !lie) return Line("bot.shift.call.m", asker);
                        return Claim(Line(Key("bot.shift.a1"), asker), BotClaimKind.Choice, LineChoice.Volunteer, sign, n);
                    }
                    if (Policy == LineBotPolicy.Honest) return Line("bot.shift.a2.h", asker, Math.Max(1, t.Needed));
                    if (Policy == LineBotPolicy.Mixed && !sign && !lie) return Line("bot.shift.call.m", asker);
                    return Claim(Line(Key("bot.shift.a2"), asker), BotClaimKind.Choice, LineChoice.Volunteer, sign, n);
                }
            }
            return null;
        }

        BotLine CrisisAnswer(LineView v, int asker, int index)
        {
            int target = bot != null ? bot.PlannedVote(v, false, v.Phase == LinePhase.CrisisCalls ? 1 : 0) : -1;
            if (Policy == LineBotPolicy.Mixed || target < 0) return Line(index == 0 ? "bot.crisis.a1.m" : "bot.crisis.a2.m", asker);
            if (Rich) return CrisisAnswerRich(v, asker, index, target);
            if (index == 0)
            {
                if (Policy == LineBotPolicy.Honest)
                    return Vote(Line(Leads(v, target) ? "bot.crisis.a1.h" : "bot.crisis.a1.h_known", asker, target), target, true, BallotKind.Crisis, 0);
                int name = target != asker ? target : Another(v, asker, target, false);
                if (name < 0) return Line("bot.crisis.a1.m", asker);
                return Vote(Line("bot.crisis.a1.g", asker, name), name, name == target, BallotKind.Crisis, 0);
            }
            if (Policy == LineBotPolicy.Honest)
            {
                if (target == asker && !Leads(v, asker)) return Vote(Line("bot.crisis.a2.h_sorry", asker), asker, true, BallotKind.Crisis, 0);
                return Line("bot.crisis.a2.h", asker);
            }
            var l = Claim(Line("bot.crisis.a2.g", asker), BotClaimKind.NotVote, asker, target != asker);
            l.ClaimBallot = BallotKind.Crisis;
            return l;
        }

        /// <summary>Crisis questions, Richest votes: Honest names its real target by money (or says it wants to win), Greedy
        /// names its target unless the asker is the target, and every Vote / NotVote answer is announced (committed).</summary>
        BotLine CrisisAnswerRich(LineView v, int asker, int index, int target)
        {
            if (index == 0)
            {
                if (Policy == LineBotPolicy.Honest)
                    return Announce(v, false, target, Vote(Line(Richest(v, target, false) ? "bot.crisis.a1.h_rich" : "bot.crisis.a1.h_win", asker, target), target, true, BallotKind.Crisis, 0));
                int named = target != asker ? target : Another(v, asker, target, false);
                if (named < 0) return Line("bot.crisis.a1.m", asker);
                return Announce(v, false, target, Vote(Line("bot.crisis.a1.g_rich", asker, named), named, named == target, BallotKind.Crisis, 0));
            }
            if (Policy == LineBotPolicy.Honest)
            {
                if (target == asker) return Announce(v, false, target, Vote(Line("bot.crisis.a2.h_sorry", asker), asker, true, BallotKind.Crisis, 0));
                // (an announced vote stands: if the asker has since become the one I think is richest, I say I keep my word)
                bool stillTrue = bot.RichestRival(v) != asker;
                var h = Claim(Line(stillTrue ? "bot.crisis.a2.h_rich" : "bot.crisis.a2.h_said", asker), BotClaimKind.NotVote, asker, true);
                h.ClaimBallot = BallotKind.Crisis;
                return Announce(v, false, target, h);
            }
            var l = Claim(Line("bot.crisis.a2.g", asker), BotClaimKind.NotVote, asker, target != asker);
            l.ClaimBallot = BallotKind.Crisis;
            return Announce(v, false, target, l);
        }

        // ================================================================== promises

        void Payments(LineView v, double now, BotTalkOutput o)
        {
            foreach (var p in promises)
            {
                if (p.Done) continue;
                if (!p.Keep) { p.Done = true; continue; }
                bool ready = false, voided = false;
                var t = Memo(p.Task);
                switch (p.When)
                {
                    case Cond.Now: ready = true; break;
                    case Cond.IfITook:
                        if (t != null && t.Revealed) { if (t.Mine == LineChoice.Take) ready = true; else voided = true; }
                        break;
                    case Cond.IfListenerVolunteers:
                        if (t != null && t.Revealed)
                        {
                            int c;
                            if (t.Choices.TryGetValue(p.To, out c) && c == LineChoice.Volunteer) ready = true; else voided = true;
                        }
                        break;
                }
                if (voided) { p.Done = true; Log(string.Format(CultureInfo.InvariantCulture, "promise_void seat={0} to={1} when={2}", Seat, p.To, p.When)); continue; }
                if (!ready) continue;
                var me = P(v, Seat);
                if (!v.Features.TransfersOpen || now < p.NotBefore || now < v.Mine.NextTransferAt || now < nextPayAt || (me != null && me.Candidate)) continue;
                if (bot != null && now < bot.TransferHoldUntil) continue;
                if (v.Mine.Prim < p.Amount)
                {
                    if (v.Phase >= LinePhase.FinalVote) { p.Done = true; Log("promise_unpaid seat=" + Seat + " to=" + p.To + " reason=funds"); }
                    continue;
                }
                p.Done = true;
                paid++;
                nextPayAt = now + config.TransferMinInterval + 0.3;
                if (bot != null) bot.NoteTransfer(now, config);
                sent.Add(new Sent { To = p.To, Amount = p.Amount, Time = now, Hush = IsHush(p.Key) });
                Log(string.Format(CultureInfo.InvariantCulture, "pay seat={0} to={1} amount={2} key={3} t={4:0.0}", Seat, p.To, p.Amount, p.Key, now));
                o.Actions.Add(Act(v, LineVerb.Transfer, p.To, p.Amount));
                return; // one transfer per tick (rate limit)
            }
        }

        // ================================================================== meeting chat

        void Meeting(LineView v, double now, IReadOnlyList<LineMessage> messages, BotTalkOutput o)
        {
            int seg = v.Meeting != null ? v.Meeting.Index : -1;
            bool meetingPhase = v.Phase >= LinePhase.MeetingDossier && v.Phase <= LinePhase.FinalVerdict && v.Phase != LinePhase.CrisisCalls;
            if (meetingPhase && seg != segment)
            {
                segment = seg;
                chatCount = 0;
                accusedAnswered = situationPlanned = defended = wealthPlanned = false;
                lastAccuser = -1;
                chatsBy.Clear();
                chat.RemoveAll(d => d.Line == null || !d.Line.Text || d.Line.Listener < 0);
            }
            if (meetingPhase) ScanMessages(v, now, messages);
            int cap = seg == 2 ? 4 : 3;
            double len = Math.Max(1, v.PhaseEndTime - v.PhaseStartTime);
            int rank = BotRank(v);
            // the situation line (victim / witness / culprit) once per meeting, staggered by bot so they don't all land at once
            if (!situationPlanned && (v.Phase == LinePhase.MeetingDiscussion || v.Phase == LinePhase.FinalDiscussion))
            {
                situationPlanned = true;
                var l = Situation(v, seg);
                if (l != null) chat.Add(new Due { At = v.PhaseStartTime + len * (0.08 + 0.12 * rank + 0.05 * U("sit" + seg)), Line = l });
            }
            // Richest votes: who I think has the most money (said with what I know when it is posted), most meetings
            if (!wealthPlanned && Rich && (v.Phase == LinePhase.MeetingDiscussion || v.Phase == LinePhase.FinalDiscussion))
            {
                wealthPlanned = true;
                if (U("rich" + seg) < 0.7)
                    chat.Add(new Due { At = v.PhaseStartTime + len * (0.30 + 0.09 * rank + 0.05 * U("richat" + seg)), Make = WealthLine });
            }
            // the defence: my turn on my feet (a subtitle over my character for everyone)
            if (!defended && v.Phase == LinePhase.FinalDefense && v.Final != null && v.Final.Defender == Seat)
            {
                defended = true;
                var l = Defence(v);
                if (l != null) chat.Insert(0, new Due { At = now + 1.0 * k, Line = l });
            }
            // the final vote: say where the vote goes before casting it
            if (v.Phase == LinePhase.FinalVote && v.Final != null && !v.Final.Revote && v.PhaseStartTime != voteKey)
            {
                voteKey = v.PhaseStartTime;
                var l = FinalVoteLine(v);
                // early in the vote (most bots have not dropped their paper yet), one bot after another
                if (l != null) chat.Insert(0, new Due { At = v.PhaseStartTime + (0.3 + 0.7 * rank + 0.2 * U("vote" + v.Final.Round)) * k, Line = l });
            }
            // my own leaked recording
            if (v.Meeting != null)
                foreach (var pick in v.Meeting.Recordings)
                {
                    if (!pick.Played || pick.Empty || (pick.Caller != Seat && pick.Callee != Seat) || leakReacted.Contains(pick.ChosenCallId)) continue;
                    leakReacted.Add(pick.ChosenCallId);
                    var l = Line(Key("bot.meet.leak"), -1);
                    // (both ends of the call react: one after the other)
                    chat.Add(new Due { At = Math.Max(now, pick.PlayAt + pick.Seconds) + (1.2 + 1.4 * rank) * k, Line = l });
                }
            // post what is due (rate limit, chat limit, at most `cap` lines per meeting besides private texts)
            chat.Sort((x, y) => x.At.CompareTo(y.At));
            for (int i = 0; i < chat.Count; i++)
            {
                var d = chat[i];
                if (d.At > now) break;
                var l = d.Make != null ? d.Make(v) : d.Line;
                if (l == null) { chat.RemoveAt(i); i--; continue; }
                bool text = l.Listener >= 0;
                if (now < v.Mine.NextMessageAt || now < nextChatAt) break;
                if (text)
                {
                    if (!v.Features.TextsOpen || v.Mine.TextsLeft <= 0) { chat.RemoveAt(i); i--; continue; }
                }
                else
                {
                    bool essential = l.Key.StartsWith("bot.vote.", StringComparison.Ordinal) || defended && v.Phase == LinePhase.FinalDefense;
                    if (!v.Features.MeetingChatOpen || v.Mine.MeetingChatLeft <= 0 || (chatCount >= cap && !essential)) { chat.RemoveAt(i); i--; continue; }
                    chatCount++;
                    chatSaid.Add(l.Key);
                }
                chat.RemoveAt(i);
                l.Text = true;
                if (!LineBotLines.Valid(l)) { Log("invalid seat=" + Seat + " key=" + l); break; }
                Said(l, now, o);
                if (text) Promised(v, l, now);
                chats++;
                nextChatAt = now + config.MessageMinInterval + 0.3;
                o.Actions.Add(text ? Act(v, LineVerb.SendText, l.Listener, text: LineBotLines.Token(l)) : Act(v, LineVerb.SendMeetingChat, text: LineBotLines.Token(l)));
                break; // one message per tick (the engine's rate limit)
            }
        }

        /// <summary>When to answer an accusation: the dossier is read first (answers come in the discussion), and the
        /// bots answer one after another.</summary>
        double ReplyAt(LineView v, double now)
        {
            double from = v.Phase == LinePhase.MeetingDossier ? v.PhaseEndTime : now;
            return from + (2.0 + 1.6 * BotRank(v)) * k;
        }

        /// <summary>My place in seat order (spreads the bots' lines over the discussion; a -bot player is not flagged as a
        /// bot by the engine, so every seat counts).</summary>
        int BotRank(LineView v)
        {
            int rank = 0;
            foreach (var p in v.Players) if (p.Id < Seat) rank++;
            return rank;
        }

        /// <summary>Spots accusations against me in new chat lines (a bot line naming me, or my name in a colleague's text).</summary>
        void ScanMessages(LineView v, double now, IReadOnlyList<LineMessage> messages)
        {
            if (messages == null) return;
            var me = P(v, Seat);
            foreach (var m in messages)
            {
                if (m == null || m.Seq <= msgSeen) continue;
                msgSeen = m.Seq;
                if (m.Kind != MessageKind.MeetingChat || m.From == Seat || m.From < 0) continue;
                int had;
                chatsBy.TryGetValue(m.From, out had);
                chatsBy[m.From] = had + 1;
                bool accuses = false;
                string key;
                List<int> args;
                // (a -bot player is not flagged as a bot by the engine: any colleague's token is a bot line)
                if (LineBotLines.TryParse(m.Text, out key, out args))
                    accuses = args.Count > 0 && args[0] == Seat && (key.StartsWith("bot.meet.victim", StringComparison.Ordinal) || key.StartsWith("bot.meet.witness", StringComparison.Ordinal)
                              || key.StartsWith("bot.meet.deflect", StringComparison.Ordinal) || key.StartsWith("bot.meet.accused", StringComparison.Ordinal)
                              || key.StartsWith("bot.meet.betrayed", StringComparison.Ordinal));
                else if (me != null) accuses = Mentions(m.Text, me.Name);
                if (!accuses) continue;
                lastAccuser = m.From;
                accusers.Add(m.From);
                // a final candidate answers in its defence (the subtitle on its feet), not twice
                if (accusedAnswered || (segment == 2 && me != null && me.Candidate && !defended)) continue;
                accusedAnswered = true;
                var l = Accused(v, m.From);
                if (l != null) chat.Add(new Due { At = ReplyAt(v, now) + (1.5 * U("acc" + m.Seq)) * k, Line = l });
            }
            // the dossier names me (whistle, guilty investigation, caught): answer once
            if (!accusedAnswered && v.Meeting != null)
                foreach (var d in v.Meeting.Dossier)
                {
                    bool named = (d.Kind == DossierKind.Whistle || d.Kind == DossierKind.Investigation) && d.Other == Seat || d.Kind == DossierKind.CashCaught && d.Player == Seat;
                    if (!named) continue;
                    accusedAnswered = true;
                    int by = d.Kind == DossierKind.CashCaught ? -1 : d.Player;
                    if (by >= 0) lastAccuser = by;
                    var l = Accused(v, by);
                    if (l != null) chat.Add(new Due { At = ReplyAt(v, now) + (1.0 + 2.0 * U("dos" + segment)) * k, Line = l });
                    break;
                }
        }

        static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var b = new StringBuilder(s.Length);
            foreach (char ch in s.ToLowerInvariant())
            {
                char c = ch;
                if (c == 'ı' || c == 'i' || c == 'İ') c = 'i';
                else if (c == 'ş') c = 's';
                else if (c == 'ğ') c = 'g';
                else if (c == 'ü') c = 'u';
                else if (c == 'ö') c = 'o';
                else if (c == 'ç') c = 'c';
                b.Append(c);
            }
            return b.ToString();
        }

        /// <summary>A colleague's free text names me ("Bot Sarı", or just "Sarı").</summary>
        static bool Mentions(string text, string name)
        {
            string t = Fold(text), n = Fold(name);
            if (n.Length < 3 || t.Length == 0) return false;
            if (t.Contains(n)) return true;
            int sp = n.LastIndexOf(' ');
            string last = sp >= 0 ? n.Substring(sp + 1) : n;
            return last.Length >= 3 && last != n && t.Contains(last);
        }

        /// <summary>Victim / witness / culprit line for meeting 1, meeting 2 (Weekend Shift) and the final discussion.</summary>
        BotLine Situation(LineView v, int seg)
        {
            var culprits = KnownCulprits(v);
            if (seg == 1)
            {
                // meeting 2: the shift came up short
                var s = MemoOf(TaskKind.WeekendShift);
                if (s != null && s.Revealed && s.Mine == LineChoice.Skip && Policy != LineBotPolicy.Honest) return Deflect(v, culprits);
                return null;
            }
            var victim = VictimLine(v);
            var witness = WitnessLine(v);
            bool took = Took(v, TaskKind.ClientBonus) || Took(v, TaskKind.PettyCash);
            if (seg == 0)
            {
                if (victim != null) return victim;
                if (witness != null) return witness;
                var betrayed = BetrayedLine(v);
                if (betrayed != null) return betrayed;
                return took && Policy != LineBotPolicy.Honest ? Deflect(v, culprits) : null;
            }
            // the final discussion: a candidate who took deflects; else what I know, if I have not said it yet
            var me = P(v, Seat);
            if (me != null && me.Candidate && took && Policy != LineBotPolicy.Honest) return Deflect(v, culprits);
            if (victim != null && !chatSaid.Contains(victim.Key)) return victim;
            if (witness != null && !chatSaid.Contains(witness.Key)) return witness;
            return null;
        }

        /// <summary>I paid a colleague to keep quiet, it kept the money and punished me anyway (the dossier says so).</summary>
        BotLine BetrayedLine(LineView v)
        {
            if (v.Meeting == null || Policy == LineBotPolicy.Honest) return null;
            foreach (var h in hushes)
            {
                if (h.At < 0) continue;
                bool punished = false;
                foreach (var d in v.Meeting.Dossier)
                    if ((d.Kind == DossierKind.Whistle || d.Kind == DossierKind.Investigation && d.N1 == 1) && d.Player == h.Target && d.Other == Seat) punished = true;
                if (!punished) continue;
                int paidOut = 0, back = 0;
                foreach (var x in v.Mine.Transfers)
                {
                    if (x.Time < h.At - 0.01) continue;
                    if (x.From == Seat && x.To == h.Target) paidOut += x.Amount;
                    if (x.From == h.Target && x.To == Seat) back += x.Amount;
                }
                if (paidOut > 0 && back == 0) return Line(Key("bot.meet.betrayed"), -1, h.Target);
            }
            return null;
        }

        BotLine VictimLine(LineView v)
        {
            var t = MemoOf(TaskKind.ClientBonus);
            if (t == null || !t.Revealed) return null;
            int thief = -1;
            foreach (int id in t.Group)
            {
                int c;
                if (id != Seat && t.Choices.TryGetValue(id, out c) && c == LineChoice.Take) { thief = id; break; }
            }
            if (thief < 0) return null;
            bool shared = t.Mine == LineChoice.Share;
            switch (Policy)
            {
                case LineBotPolicy.Honest:
                    return shared ? Claim(Line("bot.meet.victim.h", -1, thief), BotClaimKind.Choice, LineChoice.Share, true, t.Number) : null;
                case LineBotPolicy.Greedy:
                    int paidMe;
                    if (paidBy.TryGetValue(thief, out paidMe) && paidMe > 0) return Line("bot.meet.victim.g_paid", -1);
                    return Claim(Line("bot.meet.victim.g", -1, thief), BotClaimKind.Victim, thief, shared);
                default:
                    return Line("bot.meet.victim.m", -1, thief);
            }
        }

        BotLine WitnessLine(LineView v)
        {
            var t = MemoOf(TaskKind.PettyCash);
            if (t == null || !t.Revealed || t.Mine != LineChoice.Leave) return null;
            int thief = SoleTaker(t);
            if (thief < 0 || thief == Seat) return null;
            switch (Policy)
            {
                case LineBotPolicy.Honest: return Line("bot.meet.witness.h", -1, thief);
                case LineBotPolicy.Greedy:
                {
                    bool whistled = false;
                    if (v.Meeting != null) foreach (var d in v.Meeting.Dossier) if (d.Kind == DossierKind.Whistle && d.Player == Seat) whistled = true;
                    int got;
                    bool paidMe = paidBy.TryGetValue(thief, out got) && got > 0;
                    // Greedy whistles only when it was paid (deals: then on its betrayal coin) and denies the deal; paid and
                    // quiet, it saw nothing
                    if (whistled) return paidMe ? Claim(Line("bot.meet.witness.g_paid", -1, thief), BotClaimKind.Silence, 0, false) : Line("bot.meet.witness.g", -1, thief);
                    return paidMe ? Claim(Line("bot.meet.witness.g_quiet", -1), BotClaimKind.Silence, 0, false) : null;
                }
                default: return Line("bot.meet.witness.m", -1);
            }
        }

        /// <summary>Greedy blames someone it has no reason to suspect (the quietest); Mixed shrugs.</summary>
        BotLine Deflect(LineView v, HashSet<int> culprits)
        {
            if (Policy == LineBotPolicy.Mixed) return Line("bot.meet.deflect.m", -1);
            if (Policy != LineBotPolicy.Greedy) return null;
            int best = -1, fewest = int.MaxValue;
            double bestTie = 2;
            foreach (var p in v.Players)
            {
                if (p.Id == Seat || p.Fired || culprits.Contains(p.Id)) continue;
                int n;
                chatsBy.TryGetValue(p.Id, out n);
                double tie = U("quiet" + p.Id + ":" + segment);
                if (n < fewest || (n == fewest && tie < bestTie)) { best = p.Id; fewest = n; bestTie = tie; }
            }
            if (best < 0) return Line("bot.meet.deflect.m", -1);
            return Claim(Line("bot.meet.deflect.g", -1, best), BotClaimKind.Smear, best, false);
        }

        /// <summary>Answer to an accusation (or my defence): Honest denies (it never takes), Greedy turns it round, Mixed
        /// admits and points at someone else it knows took.</summary>
        BotLine Accused(LineView v, int accuser)
        {
            bool took = Took(v, TaskKind.ClientBonus) || Took(v, TaskKind.PettyCash);
            var shift = MemoOf(TaskKind.WeekendShift);
            bool skipped = shift != null && shift.Revealed && shift.Failed && shift.Mine == LineChoice.Skip;
            var culprits = KnownCulprits(v);
            switch (Policy)
            {
                case LineBotPolicy.Honest:
                    return Claim(Line("bot.meet.accused.h", -1), BotClaimKind.Innocent, 0, !took);
                case LineBotPolicy.Greedy:
                    if (accuser >= 0 && accuser != Seat) return Claim(Line("bot.meet.accused.g", -1, accuser), BotClaimKind.Smear, accuser, false);
                    return Deflect(v, culprits);
                default:
                    if (!took && !skipped) return Claim(Line("bot.meet.accused.h", -1), BotClaimKind.Innocent, 0, true);
                    int other = -1;
                    foreach (int id in culprits) if (id != Seat && id != accuser && (other < 0 || id < other)) other = id;
                    if (other < 0) foreach (int id in culprits) if (id != Seat) { other = id; break; }
                    if (took && other >= 0) return Line("bot.meet.accused.m", -1, other);
                    return Line("bot.meet.deflect.m", -1);
            }
        }

        BotLine Defence(LineView v) { return Accused(v, lastAccuser); }

        /// <summary>
        /// The wealth suspicion in the meeting chat (Richest votes): the colleague this bot believes has the most money,
        /// from its own ledger at the moment it speaks (the final discussion: among the candidates). Honest says it only when
        /// it believes it (not when it thinks it is the richest itself); Greedy names its real pick (it wants that desk);
        /// Mixed names someone else when its coin for the meeting says lie.
        /// </summary>
        BotLine WealthLine(LineView v)
        {
            if (bot == null || (v.Phase != LinePhase.MeetingDossier && v.Phase != LinePhase.MeetingDiscussion && v.Phase != LinePhase.FinalDiscussion)) return null;
            var me = P(v, Seat);
            if (me == null || me.Fired) return null;
            bool final = segment == 2 && v.Final != null && v.Final.Candidates.Count > 0;
            List<int> pool = null;
            if (final)
            {
                pool = new List<int>();
                foreach (int id in v.Final.Candidates) { var p = P(v, id); if (p != null && !p.Fired && id != Seat) pool.Add(id); }
            }
            int top = bot.RichestRival(v, pool);
            if (top < 0) return null;
            switch (Policy)
            {
                case LineBotPolicy.Honest:
                    return Richest(v, top, final) ? Line(final ? "bot.meet.richc.h" : "bot.meet.rich.h", -1, top) : null;
                case LineBotPolicy.Greedy:
                    return Line("bot.meet.rich.g", -1, top);
                default:
                {
                    int named = top;
                    if (MixedLies("rich" + segment))
                    {
                        var others = pool != null ? new List<int>(pool) : new List<int>();
                        if (pool == null) foreach (var p in v.Players) if (!p.Fired && p.Id != Seat) others.Add(p.Id);
                        others.Remove(top);
                        others.Sort();
                        if (others.Count > 0) named = others[(int)(U("richother" + segment) * others.Count) % others.Count];
                    }
                    var l = Line("bot.meet.rich.m", -1, named);
                    l.Truthful = named == top;
                    return l;
                }
            }
        }

        BotLine FinalVoteLine(LineView v)
        {
            var me = P(v, Seat);
            if (me == null || me.Fired || v.Mine.Ballots.Count == 0) return null;
            if (Policy == LineBotPolicy.Greedy) return Line("bot.vote.g", -1);
            int target = bot != null ? bot.PlannedVote(v, true, 0) : -1;
            if (target < 0) return null;
            int round = v.Final.Round;
            if (Rich)
            {
                if (Policy == LineBotPolicy.Honest)
                {
                    bool richest = Richest(v, target, true) && U("hwin" + round) >= 0.35;
                    return Announce(v, true, target, Vote(Line(richest ? "bot.vote.h_rich" : "bot.vote.h_win", -1, target), target, true, BallotKind.Final, round));
                }
                int named = target;
                if (MixedLies("final" + round))
                {
                    int other = Another(v, -1, target, true);
                    if (other >= 0) named = other;
                }
                return Announce(v, true, target, Vote(Line("bot.vote.m_rich", -1, named), named, named == target, BallotKind.Final, round));
            }
            if (Policy == LineBotPolicy.Honest)
                return Vote(Line(Leads(v, target) ? "bot.vote.h" : "bot.vote.h_known", -1, target), target, true, BallotKind.Final, round);
            int name = target;
            if (MixedLies("final" + round))
            {
                int other = Another(v, -1, target, true);
                if (other >= 0) name = other;
            }
            return Vote(Line("bot.vote.m", -1, name), name, name == target, BallotKind.Final, round);
        }

        // ================================================================== results: checks and the log

        /// <summary>At Results everything is public: compare every claim with what really happened, then release the log.</summary>
        void Finish(LineView v, BotTalkOutput o)
        {
            flushed = true;
            int checks = 0, mismatches = 0;
            var r = v.Results;
            if (r != null)
                foreach (var l in claims)
                {
                    bool known;
                    bool actual = Actual(r, l, out known);
                    if (!known) continue;
                    checks++;
                    bool ok = actual == l.Truthful;
                    if (!ok) mismatches++;
                    Log(string.Format(CultureInfo.InvariantCulture, "check seat={0} policy={1} key={2} claim={3} truthful={4} actual={5} ok={6}",
                        Seat, Policy, l.Key, l.Claim, Yes(l.Truthful), Yes(actual), Yes(ok)));
                }
            Log(string.Format(CultureInfo.InvariantCulture,
                "summary seat={0} policy={1} lines={2} truthful={3} lies={4} calls={5} answered={6} refused={7} answers={8} chat={9} paid={10} broken={11} checks={12} mismatches={13}",
                Seat, Policy, lines, lines - lies, lies, calls, answeredCalls, refusedCalls, answers, chats, paid, broken, checks, mismatches));
            if (r != null) { LogVotes(r); LogDeals(v, r); }
            FinishPranks(v);
            o.Log.AddRange(pendingLog);
            pendingLog.Clear();
        }

        /// <summary>
        /// Every ballot of mine against the true balances when its vote started (the results' prim log): "richest" = the
        /// options with the most prim then, hit = my ballot named one of them (verify_logs.py adds them up per policy).
        /// </summary>
        void LogVotes(LineWeekResult r)
        {
            string style = bot != null ? bot.VoteStyle.ToString() : "None";
            foreach (var b in r.Ballots)
            {
                if (b.Voter != Seat || b.SelfAssigned || b.Target < 0) continue;
                VoteStart start = null;
                foreach (var s in voteStarts)
                    if (s.Kind == b.Kind && s.Round == b.Round && s.Revote == (b.Kind == BallotKind.Final && b.Revote)) { start = s; break; }
                if (start == null || start.Options.Count == 0) continue;
                int best = int.MinValue;
                var prim = new Dictionary<int, int>();
                foreach (int id in start.Options)
                {
                    int x = config.StartPrim;
                    foreach (var c in r.PrimLog) if (c.Player == id && c.Time <= start.Time) x += c.Delta;
                    prim[id] = x;
                    best = Math.Max(best, x);
                }
                var richest = new List<int>();
                foreach (int id in start.Options) if (prim[id] == best) richest.Add(id);
                Log(string.Format(CultureInfo.InvariantCulture, "vote seat={0} policy={1} style={2} ballot={3} round={4} revote={5} index={6} target={7} target_prim={8} richest={9} richest_prim={10} hit={11}",
                    Seat, Policy, style, b.Kind, b.Round, Yes(b.Revote), b.Index, b.Target, prim.ContainsKey(b.Target) ? prim[b.Target] : -1,
                    string.Join(",", richest), best, Yes(richest.Contains(b.Target))));
            }
        }

        /// <summary>
        /// Silence deals at Results (verify_logs.py): every hush payment I made ("bottalk hush ... punished=yes|no
        /// returned=N") and, for every colleague I could punish, what it paid me until meeting 1 ended, what I sent back
        /// and whether I punished it ("bottalk punish ... outcome=none|refused|kept|betrayed").
        /// </summary>
        void LogDeals(LineView v, LineWeekResult r)
        {
            double end = double.IsNaN(dealsEnd) ? double.MaxValue : dealsEnd;
            foreach (var h in hushes)
            {
                bool punished = Punished(r, h.Target, Seat, h.Kind == TaskKind.PettyCash);
                int back = 0;
                foreach (var x in r.Transfers) if (x.From == h.Target && x.To == Seat && x.Time >= h.At - 0.01 && x.Time < end) back += x.Amount;
                Log(string.Format(CultureInfo.InvariantCulture, "hush seat={0} policy={1} kind={2} to={3} amount=1 via={4} punished={5} returned={6}",
                    Seat, Policy, h.Kind, h.Target, h.At < 0 ? "none" : h.Via, Yes(punished), back));
            }
            if (bot == null || bot.PunishStyle != LineBotPunishStyle.Deals) return;
            var mineSent = MatchSent(r);
            foreach (var p in v.Players)
            {
                for (int w = 0; w < 2; w++)
                {
                    bool whistle = w == 1;
                    if (p.Id == Seat || !(whistle ? bot.Witness(p.Id) : bot.Victim(p.Id))) continue;
                    var t = MemoOf(whistle ? TaskKind.PettyCash : TaskKind.ClientBonus);
                    double since = t != null && !double.IsNaN(t.Start) ? t.Start : double.MinValue;
                    int got = 0, back = 0;
                    for (int i = 0; i < r.Transfers.Count; i++)
                    {
                        var x = r.Transfers[i];
                        if (x.Time < since - 1e-9 || x.Time >= end) continue;
                        if (x.From == p.Id && x.To == Seat) got += x.Amount;
                        // the bot's own returns (not a payment of mine: silence money I owed it, a promise)
                        if (x.From == Seat && x.To == p.Id && !mineSent.ContainsKey(i)) back += x.Amount;
                    }
                    bool mine = Punished(r, Seat, p.Id, whistle), other = false, spent = false;
                    foreach (var q in v.Players)
                    {
                        if (q.Id != Seat && q.Id != p.Id && Punished(r, q.Id, p.Id, whistle)) other = true;
                        // one investigation per match: used on the other taker of my trio
                        if (!whistle && q.Id != p.Id && q.Id != Seat && Punished(r, Seat, q.Id, false)) spent = true;
                    }
                    bool honest = bot.HonestMode(v, whistle, p.Id);
                    string outcome = got == 0 ? "none" : back >= got ? "refused" : mine ? "betrayed" : "kept";
                    Log(string.Format(CultureInfo.InvariantCulture, "punish seat={0} policy={1} kind={2} target={3} mode={4} paid={5} returned={6} punished={7} by_other={8} spent={9} outcome={10}",
                        Seat, Policy, whistle ? "whistle" : "investigation", p.Id, honest ? "honest" : "greedy", got, Math.Min(back, got), Yes(mine), Yes(other), Yes(spent), outcome));
                }
            }
        }

        /// <summary>
        /// The results' transfers (index → was it silence money) that are the payments this talker sent: each payment matches
        /// the first unclaimed transfer of mine to the same colleague and amount, at or after the moment I sent it (a -bot
        /// process sends on its own clock, the engine stamps the host's: up to a second early, a few late).
        /// </summary>
        Dictionary<int, bool> MatchSent(LineWeekResult r)
        {
            var claimed = new Dictionary<int, bool>();
            foreach (var s in sent)
                for (int i = 0; i < r.Transfers.Count; i++)
                {
                    var x = r.Transfers[i];
                    if (claimed.ContainsKey(i) || x.From != Seat || x.To != s.To || x.Amount != s.Amount || x.Time < s.Time - 1.0 || x.Time > s.Time + 5.0) continue;
                    claimed[i] = s.Hush;
                    break;
                }
            return claimed;
        }

        static bool Punished(LineWeekResult r, int by, int target, bool whistle)
        {
            foreach (var d in r.Incidents)
                if (d.Player == by && d.Other == target && (whistle ? d.Kind == DossierKind.Whistle : d.Kind == DossierKind.Investigation)) return true;
            return false;
        }

        /// <summary>Whether a claim came true (known false when the results cannot tell).</summary>
        bool Actual(LineWeekResult r, BotLine l, out bool known)
        {
            known = true;
            switch (l.Claim)
            {
                case BotClaimKind.Choice:
                    foreach (var c in r.Choices) if (c.TaskNumber == l.ClaimTask && c.Player == Seat) return c.Choice == l.ClaimValue;
                    break;
                case BotClaimKind.Vote:
                case BotClaimKind.NotVote:
                    foreach (var b in r.Ballots)
                    {
                        if (b.Voter != Seat || b.Kind != l.ClaimBallot) continue;
                        if (b.Kind == BallotKind.Final && (b.Round != l.ClaimRound || b.Revote)) continue;
                        return l.Claim == BotClaimKind.Vote ? b.Target == l.ClaimValue : b.Target != l.ClaimValue;
                    }
                    break;
                case BotClaimKind.Pay:
                {
                    var when = (Cond)l.ClaimRound;
                    bool due = true;
                    if (when == Cond.IfITook)
                    {
                        due = false;
                        foreach (var c in r.Choices) if (c.TaskNumber == l.ClaimTask && c.Player == Seat) due = c.Choice == LineChoice.Take;
                    }
                    if (!due) return l.Truthful; // nothing was owed: the promise stands as said
                    bool hush = IsHush(l.Key);
                    var mineSent = MatchSent(r);
                    for (int i = 0; i < r.Transfers.Count; i++)
                    {
                        var t = r.Transfers[i];
                        bool wasHush;
                        if (!hush && mineSent.TryGetValue(i, out wasHush) && wasHush) continue; // silence money keeps no other promise
                        if (t.From == Seat && t.To == l.Listener && t.Time >= l.Time - 0.01 && t.Amount >= l.ClaimValue) return true;
                    }
                    // a promise can run out of time or money in a short match: only a kept promise is checked
                    if (l.Truthful) { known = false; return false; }
                    return false;
                }
            }
            known = false;
            return false;
        }
    }
}
