using System;
using System.Collections.Generic;
using System.Text;

namespace BirimizKovulacak.Core
{
    // v5 bot talk (docs/V5_BOT_TALK.md): bots exist for testing only. They talk on the desk phone and in the meeting chat
    // with lines chosen from what they really do. Presentation only: lines never enter the rules engine except as meeting
    // chat / text messages (a token, rendered by each client in its own language).

    /// <summary>What a line says about the speaker's own behaviour (checked against what the bot really does).</summary>
    public enum BotClaimKind : byte
    {
        None = 0,
        /// <summary>"I share / leave / volunteer / take / skip": ClaimTask, ClaimValue = <see cref="LineChoice"/>.</summary>
        Choice = 1,
        /// <summary>"My vote goes to X": ClaimValue = X, ClaimBallot and ClaimRound.</summary>
        Vote = 2,
        /// <summary>"I won't vote for you": ClaimValue = the listener.</summary>
        NotVote = 3,
        /// <summary>"I would report it."</summary>
        Whistle = 4,
        /// <summary>"I'd keep quiet (for prim)." Bots always report a theft they witness.</summary>
        Silence = 5,
        /// <summary>"I'll send you N prim": ClaimValue = N (kept or not per policy).</summary>
        Pay = 6,
        /// <summary>A made-up accusation against someone the speaker has no reason to suspect.</summary>
        Smear = 7,
        /// <summary>"X robbed me": false when the speaker took as well.</summary>
        Victim = 8,
        /// <summary>"I didn't do anything."</summary>
        Innocent = 9,
        /// <summary>"I'll call you back."</summary>
        Callback = 10,
    }

    /// <summary>The question buttons a human gets while on the phone with a bot (keys 1/2/3).</summary>
    public enum BotQuestionSet : byte { None = 0, ClientBonus = 1, PettyCash = 2, WeekendShift = 3, Crisis = 4 }

    /// <summary>One bot line: a localisation key (without the "v5." prefix) and its arguments.</summary>
    public sealed class BotLine
    {
        public string Key = "";
        public int Speaker = LineIds.None;
        /// <summary>Phone: who hears it. -1 = the meeting chat (everyone); a private text otherwise when <see cref="Text"/>.</summary>
        public int Listener = LineIds.None;
        /// <summary>Per <see cref="LineBotLines.Signature"/>: a player id ('p') or a number ('n').</summary>
        public readonly List<int> Args = new List<int>();
        public LineBotPolicy Policy;
        /// <summary>Whether the line agrees with what the speaker really does (computed from its own view and decisions).</summary>
        public bool Truthful = true;
        public BotClaimKind Claim;
        public int ClaimTask = -1;
        public int ClaimValue = -1;
        public BallotKind ClaimBallot;
        public int ClaimRound;
        /// <summary>Sent as a meeting chat line (Listener -1) or a private text (Listener ≥ 0) instead of on the phone.</summary>
        public bool Text;
        /// <summary>Said out loud in the meeting room (a reaction to a prank): over the head for everyone, never in the chat.</summary>
        public bool Shout;
        public double Time;

        public override string ToString()
        {
            var b = new StringBuilder(Key);
            foreach (int a in Args) b.Append(',').Append(a);
            return b.ToString();
        }
    }

    /// <summary>A question one party of a call asks the other (a human's button, or a bot's simulated question).</summary>
    public struct BotAsk
    {
        public int From, To;
        public BotQuestionSet Set;
        public int Index;
    }

    /// <summary>What a <see cref="LineBotTalker"/> wants done after a tick.</summary>
    public sealed class BotTalkOutput
    {
        /// <summary>Engine verbs (call, answer, decline, hang up, transfer, meeting chat, text). Request ids are the
        /// caller's job (restamp them per seat).</summary>
        public readonly List<LineAction> Actions = new List<LineAction>();
        /// <summary>Phone lines to deliver to <see cref="BotLine.Listener"/> (presentation only).</summary>
        public readonly List<BotLine> Lines = new List<BotLine>();
        /// <summary>Every line said this tick (phone, meeting chat, texts): tests and the log.</summary>
        public readonly List<BotLine> Said = new List<BotLine>();
        /// <summary>Simulated questions to another bot in my call.</summary>
        public readonly List<BotAsk> Asks = new List<BotAsk>();
        /// <summary>Log lines (released at Results: they say whether a bot lied).</summary>
        public readonly List<string> Log = new List<string>();
        /// <summary>Yaramazlıklar: lines said out loud in the meeting room (a reaction to a prank): over the speaker's head for
        /// everyone, with its mumble (presentation only, never through the engine).</summary>
        public readonly List<BotLine> Shouts = new List<BotLine>();

        public void Clear() { Actions.Clear(); Lines.Clear(); Said.Clear(); Asks.Clear(); Log.Clear(); Shouts.Clear(); }
    }

    /// <summary>
    /// The line catalogue: every key a bot can say, its argument signature, the question buttons per context, and the
    /// chat token that carries a line through the engine's free-text channels (rendered by each client in its language).
    /// Localisation: "v5." + key in tr.v5.json / en.v5.json.
    /// </summary>
    public static class LineBotLines
    {
        /// <summary>Key → argument signature ('p' = player id rendered as a name, 'n' = number).</summary>
        static readonly Dictionary<string, string> Signatures = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // phone, every task
            { "bot.answer.h", "p" }, { "bot.answer.g", "" }, { "bot.answer.m", "" },
            { "bot.refuse.g", "" }, { "bot.refuse.m", "" },
            { "bot.bye.h", "" }, { "bot.bye.g", "" }, { "bot.bye.m", "" },
            { "bot.thanks.h", "" }, { "bot.thanks.g", "" }, { "bot.thanks.m", "" },
            // Client Bonus
            { "bot.cb.call.h", "p" }, { "bot.cb.call.g", "" }, { "bot.cb.call.m", "" },
            { "bot.cb.a1.h", "" }, { "bot.cb.a1.g", "" }, { "bot.cb.a1.m", "" }, { "bot.cb.a1.m_dodge", "" },
            { "bot.cb.a2.h", "" }, { "bot.cb.a2.g", "" }, { "bot.cb.a2.m", "" },
            { "bot.cb.a3.h", "" }, { "bot.cb.a3.g", "" }, { "bot.cb.a3.m", "" },
            { "bot.cb.victim.h", "" }, { "bot.cb.victim.g", "" }, { "bot.cb.victim.m", "" },
            { "bot.cb.thief.g", "" }, { "bot.cb.thief.m", "" },
            // Petty Cash
            { "bot.cash.call.h", "" }, { "bot.cash.call.g", "" }, { "bot.cash.call.m", "" },
            { "bot.cash.a1.h", "" }, { "bot.cash.a1.g", "" }, { "bot.cash.a1.m", "" },
            { "bot.cash.a2.h", "" }, { "bot.cash.a2.g", "" }, { "bot.cash.a2.m", "" },
            { "bot.cash.a3.h", "" }, { "bot.cash.a3.g", "" }, { "bot.cash.a3.m", "" },
            { "bot.cash.witness.h", "" }, { "bot.cash.witness.g", "" }, { "bot.cash.witness.m", "" },
            { "bot.cash.thief.g", "" }, { "bot.cash.thief.m", "" },
            // Weekend Shift
            { "bot.shift.call.h", "n" }, { "bot.shift.call.h_skip", "n" }, { "bot.shift.call.g", "" }, { "bot.shift.call.m", "" },
            { "bot.shift.a1.h", "" }, { "bot.shift.a1.h_skip", "" }, { "bot.shift.a1.g", "" }, { "bot.shift.a1.m", "" },
            { "bot.shift.a2.h", "n" }, { "bot.shift.a2.g", "" }, { "bot.shift.a2.m", "" },
            { "bot.shift.short.h", "" }, { "bot.shift.short.g", "" }, { "bot.shift.short.m", "" },
            // Crisis
            { "bot.crisis.call.h", "p" }, { "bot.crisis.call.h_known", "p" }, { "bot.crisis.call.g", "p" }, { "bot.crisis.call.m", "" },
            { "bot.crisis.a1.h", "p" }, { "bot.crisis.a1.h_known", "p" }, { "bot.crisis.a1.g", "p" }, { "bot.crisis.a1.m", "" },
            { "bot.crisis.a2.h", "" }, { "bot.crisis.a2.h_sorry", "" }, { "bot.crisis.a2.g", "" }, { "bot.crisis.a2.m", "" },
            // Crisis, Richest votes (LineBotVoteStyle.Richest): explained by money
            { "bot.crisis.call.h_rich", "p" }, { "bot.crisis.call.h_win", "p" }, { "bot.crisis.call.g_rich", "p" },
            { "bot.crisis.a1.h_rich", "p" }, { "bot.crisis.a1.h_win", "p" }, { "bot.crisis.a1.g_rich", "p" }, { "bot.crisis.a2.h_rich", "" }, { "bot.crisis.a2.h_said", "" },
            // meetings (chat) and the final
            { "bot.meet.victim.h", "p" }, { "bot.meet.victim.g", "p" }, { "bot.meet.victim.g_paid", "" }, { "bot.meet.victim.m", "p" },
            { "bot.meet.witness.h", "p" }, { "bot.meet.witness.g", "p" }, { "bot.meet.witness.g_paid", "p" }, { "bot.meet.witness.g_quiet", "" },
            { "bot.meet.witness.m", "" },
            { "bot.meet.deflect.g", "p" }, { "bot.meet.deflect.m", "" },
            { "bot.meet.accused.h", "" }, { "bot.meet.accused.g", "p" }, { "bot.meet.accused.m", "p" },
            { "bot.meet.leak.h", "" }, { "bot.meet.leak.g", "" }, { "bot.meet.leak.m", "" },
            { "bot.vote.h", "p" }, { "bot.vote.h_known", "p" }, { "bot.vote.g", "" }, { "bot.vote.m", "p" },
            // Richest votes: the wealth suspicion in the meeting chat, the final vote by money
            { "bot.meet.rich.h", "p" }, { "bot.meet.richc.h", "p" }, { "bot.meet.rich.g", "p" }, { "bot.meet.rich.m", "p" },
            { "bot.vote.h_rich", "p" }, { "bot.vote.h_win", "p" }, { "bot.vote.m_rich", "p" },
            // Yaramazlıklar (docs/V5_YARAMAZLIKLAR.md §7): reactions over the head when pranked, a paper plane's note
            { "bot.prank.coffee.h", "" }, { "bot.prank.coffee.g", "" }, { "bot.prank.coffee.m", "" },
            { "bot.prank.postit.h", "" }, { "bot.prank.postit.g", "" }, { "bot.prank.postit.m", "" },
            { "bot.prank.phone.h", "" }, { "bot.prank.phone.g", "" }, { "bot.prank.phone.m", "" },
            { "bot.prank.plane.h", "" }, { "bot.prank.plane.g", "" }, { "bot.prank.plane.m", "" },
            { "bot.prank.cake.h", "" }, { "bot.prank.cake.g", "" }, { "bot.prank.cake.m", "" },
            { "bot.prank.tutanak.h", "" }, { "bot.prank.tutanak.g", "" }, { "bot.prank.tutanak.m", "" },
            { "bot.prank.note.m1", "" }, { "bot.prank.note.m2", "" },
            // the question buttons
            { "bot.q.cb.1", "" }, { "bot.q.cb.2", "" }, { "bot.q.cb.3", "" },
            { "bot.q.cash.1", "" }, { "bot.q.cash.2", "" }, { "bot.q.cash.3", "" },
            { "bot.q.shift.1", "" }, { "bot.q.shift.2", "" },
            { "bot.q.crisis.1", "" }, { "bot.q.crisis.2", "" },
        };

        /// <summary>Every key (localisation check, tests).</summary>
        public static IEnumerable<string> Keys => Signatures.Keys;
        public static bool Known(string key) { return key != null && Signatures.ContainsKey(key); }

        /// <summary>Argument signature of a key ('p' player, 'n' number); null for an unknown key.</summary>
        public static string Signature(string key)
        {
            string s;
            return key != null && Signatures.TryGetValue(key, out s) ? s : null;
        }

        /// <summary>True when the line has exactly the arguments its key needs (players in range).</summary>
        public static bool Valid(BotLine line)
        {
            var sig = Signature(line != null ? line.Key : null);
            if (sig == null || line.Args.Count != sig.Length) return false;
            for (int i = 0; i < sig.Length; i++)
                if (sig[i] == 'p' ? line.Args[i] < 0 || line.Args[i] >= LineProtocol.MaxPlayers : line.Args[i] < 0) return false;
            return true;
        }

        // ------------------------------------------------------------------ questions

        static readonly string[][] QuestionKeys =
        {
            new string[0],
            new[] { "bot.q.cb.1", "bot.q.cb.2", "bot.q.cb.3" },
            new[] { "bot.q.cash.1", "bot.q.cash.2", "bot.q.cash.3" },
            new[] { "bot.q.shift.1", "bot.q.shift.2" },
            new[] { "bot.q.crisis.1", "bot.q.crisis.2" },
        };

        /// <summary>The question buttons for a context (empty for None).</summary>
        public static IReadOnlyList<string> Questions(BotQuestionSet set)
        {
            int i = (int)set;
            return i >= 0 && i < QuestionKeys.Length ? QuestionKeys[i] : QuestionKeys[0];
        }

        /// <summary>What you can ask a bot right now: the current task while choosing, the crisis vote during the crisis calls.</summary>
        public static BotQuestionSet QuestionSetOf(LineView v)
        {
            if (v == null) return BotQuestionSet.None;
            if (v.Phase == LinePhase.CrisisCalls) return BotQuestionSet.Crisis;
            if (v.Phase != LinePhase.TaskPlay || v.Task == null) return BotQuestionSet.None;
            switch (v.Task.Kind)
            {
                case TaskKind.ClientBonus: return BotQuestionSet.ClientBonus;
                case TaskKind.PettyCash: return BotQuestionSet.PettyCash;
                case TaskKind.WeekendShift: return BotQuestionSet.WeekendShift;
                default: return BotQuestionSet.None;
            }
        }

        // ------------------------------------------------------------------ chat token

        /// <summary>Marks a bot line inside a chat / text message (U+2063 survives the engine's text sanitiser).</summary>
        public const string TokenPrefix = "⁣bot|";

        /// <summary>"⁣bot|key|arg|arg": what a bot sends as meeting chat or text (each client renders it in its language).</summary>
        public static string Token(BotLine line)
        {
            var b = new StringBuilder(TokenPrefix).Append(line.Key);
            foreach (int a in line.Args) b.Append('|').Append(a.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return b.ToString();
        }

        /// <summary>Reads a token back (false for any other text; unknown keys and wrong arguments are rejected).</summary>
        public static bool TryParse(string text, out string key, out List<int> args)
        {
            key = null;
            args = null;
            if (text == null || !text.StartsWith(TokenPrefix, StringComparison.Ordinal)) return false;
            var parts = text.Substring(TokenPrefix.Length).Split('|');
            var sig = Signature(parts[0]);
            if (sig == null || parts.Length - 1 != sig.Length) return false;
            var list = new List<int>();
            for (int i = 1; i < parts.Length; i++)
            {
                int n;
                if (!int.TryParse(parts[i], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out n)) return false;
                if (sig[i - 1] == 'p' ? n < 0 || n >= LineProtocol.MaxPlayers : n < 0 || n > 999) return false;
                list.Add(n);
            }
            key = parts[0];
            args = list;
            return true;
        }

        /// <summary>The policy suffix of a key ("bot.cb.call.g" → Greedy); Honest for keys without one.</summary>
        public static string Suffix(LineBotPolicy p) { return p == LineBotPolicy.Greedy ? ".g" : p == LineBotPolicy.Mixed ? ".m" : ".h"; }
    }
}
