using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests.Line
{
    /// <summary>
    /// v5 bot talk (docs/V5_BOT_TALK.md): the previews never change what a bot does, the talker's lines match the bot's
    /// real choices (Honest never lies, Greedy lies about taking), lines only name what the bot could know, every
    /// placeholder is filled, and the talker's own calls / payments / chat go through the engine without errors.
    /// </summary>
    public sealed class LineBotTalkTests
    {
        static readonly LineBotPolicy[] Rotation = { LineBotPolicy.Honest, LineBotPolicy.Greedy, LineBotPolicy.Mixed };

        sealed class Sim
        {
            public ILineEngine Engine;
            public LineConfig Config;
            public LineBot[] Bots;
            public LineBotTalker[] Talkers;
            public LineBotPolicy[] Policy;
            public readonly List<BotLine> Lines = new List<BotLine>();
            public readonly List<string> Log = new List<string>();
            public readonly List<LineActionResult> Rejected = new List<LineActionResult>();
            public readonly List<string> RejectedWhat = new List<string>();
            public readonly uint[] Requests = new uint[6];
            /// <summary>Per seat: colleagues whose taking this seat could see (own group reveals, public dossier).</summary>
            public readonly HashSet<int>[] Visible = new HashSet<int>[6];
            public readonly List<string> Leaks = new List<string>();
            public int Asks, PhoneLines, Transfers;
            public LineWeekResult Result;
        }

        static Sim Run(int players, int seed, Func<int, LineBotPolicy> policy, bool talk = true, bool previews = false, double think = 4, float scale = 0.3f)
        {
            var setup = new LineSetup { WeekId = 1, MatchId = "talk-" + players + "-" + seed, Seed = seed };
            for (int i = 0; i < players; i++) setup.Players.Add(new LinePlayerInfo { Id = i, Name = "Bot " + i, ColorIndex = i, IsBot = true });
            var config = LineConfig.CreateDefault();
            config.Durations.Scale(scale);
            var sim = new Sim { Engine = LineEngineFactory.Create(setup, config), Config = config, Bots = new LineBot[players], Talkers = new LineBotTalker[players], Policy = new LineBotPolicy[players] };
            for (int i = 0; i < players; i++)
            {
                sim.Policy[i] = policy(i);
                sim.Bots[i] = new LineBot(i, seed * 31 + i, sim.Policy[i]) { ThinkSeconds = think };
                sim.Talkers[i] = new LineBotTalker(i, seed * 17 + i, sim.Policy[i], sim.Bots[i]);
                sim.Visible[i] = new HashSet<int>();
            }
            var o = new BotTalkOutput();
            double now = 0;
            for (int step = 0; step < 20000 && !sim.Engine.IsFinished; step++)
            {
                sim.Engine.Tick(now);
                sim.Engine.DrainEvents();
                for (int round = 0; round < 8; round++)
                {
                    bool acted = false;
                    for (int i = 0; i < players; i++)
                    {
                        var view = sim.Engine.BuildView(i, now);
                        if (previews)
                        {
                            sim.Bots[i].PlannedTaskChoice(view);
                            sim.Bots[i].PlannedVote(view, false, view.Phase == LinePhase.CrisisCalls ? 1 : 0);
                            sim.Bots[i].PlannedVote(view, true);
                        }
                        var a = sim.Bots[i].Next(view, config, now);
                        if (a != null) { Apply(sim, i, a, now); acted = true; }
                    }
                    if (!acted) break;
                }
                if (talk)
                    for (int i = 0; i < players; i++)
                    {
                        var view = sim.Engine.BuildView(i, now);
                        Observe(sim, i, view);
                        o.Clear();
                        sim.Talkers[i].Tick(view, config, now, sim.Engine.MessagesFor(i, sim.Talkers[i].MessagesSeen), o);
                        foreach (var a in o.Actions) Apply(sim, i, a, now);
                        foreach (var ask in o.Asks) { sim.Asks++; sim.Talkers[ask.To].Hear(ask); }
                        sim.PhoneLines += o.Lines.Count;
                        foreach (var l in o.Said) CheckVisible(sim, l);
                        sim.Lines.AddRange(o.Said);
                        sim.Log.AddRange(o.Log);
                    }
                now += 0.25;
            }
            Assert.IsTrue(sim.Engine.IsFinished, "match did not finish");
            sim.Result = sim.Engine.Result;
            if (talk)
            {
                // Results: every talker releases its log (claims checked against the full disclosure)
                for (int i = 0; i < players; i++)
                {
                    o.Clear();
                    sim.Talkers[i].Tick(sim.Engine.BuildView(i, now), config, now, null, o);
                    sim.Log.AddRange(o.Log);
                }
            }
            return sim;
        }

        static void Apply(Sim sim, int seat, LineAction a, double now)
        {
            var stamped = new LineAction(a.WeekId, ++sim.Requests[seat], a.Verb, a.Target, a.A, a.B, a.C, a.Text);
            var r = sim.Engine.Apply(seat, stamped, now);
            if (a.Verb == LineVerb.Transfer && r.Accepted) sim.Transfers++;
            if (!r.Accepted) { sim.Rejected.Add(r); sim.RejectedWhat.Add(seat + " " + stamped + " in " + sim.Engine.Phase + ": " + r.Failure); }
        }

        /// <summary>What this seat could see of who took: its own revealed groups and the public dossier.</summary>
        static void Observe(Sim sim, int seat, LineView v)
        {
            if (v.Task != null && v.Task.Revealed && v.Task.Kind != TaskKind.WeekendShift)
                foreach (var c in v.Task.Choices) if (c.Choice == LineChoice.Take) sim.Visible[seat].Add(c.Player);
            if (v.Meeting != null)
                foreach (var d in v.Meeting.Dossier)
                {
                    if (d.Kind == DossierKind.CashCaught) sim.Visible[seat].Add(d.Player);
                    if (d.Kind == DossierKind.Whistle || d.Kind == DossierKind.Investigation && d.N1 == 1) sim.Visible[seat].Add(d.Other);
                }
        }

        /// <summary>A line that names someone as having taken may only name a colleague the speaker saw take.</summary>
        static void CheckVisible(Sim sim, BotLine l)
        {
            bool accuses = l.Key.StartsWith("bot.meet.victim.", StringComparison.Ordinal) && l.Key != "bot.meet.victim.g_paid"
                           || l.Key == "bot.meet.witness.h" || l.Key == "bot.meet.witness.g" || l.Key == "bot.meet.witness.g_paid" || l.Key == "bot.meet.accused.m";
            if (accuses && !sim.Visible[l.Speaker].Contains(l.Args[0])) sim.Leaks.Add(l.Speaker + " " + l);
        }

        static IEnumerable<Sim> Matches(int count, int seedBase, bool talk = true)
        {
            for (int n = 3; n <= 6; n++)
                for (int i = 0; i < count; i++)
                {
                    int seed = seedBase + n * 1000 + i;
                    int shift = i % 3;
                    yield return Run(n, seed, s => Rotation[(s + shift) % 3], talk);
                }
        }

        // ------------------------------------------------------------------ previews never change a decision

        [Test]
        public void Previews_DoNotChangeDecisions()
        {
            foreach (double think in new[] { 0.0, 4.0, 15.0 })
                for (int n = 3; n <= 6; n++)
                    for (int seed = 1; seed <= 4; seed++)
                    {
                        int shift = seed % 3;
                        Func<int, LineBotPolicy> pol = s => Rotation[(s + shift) % 3];
                        var plain = Run(n, seed * 7 + n, pol, talk: false, previews: false, think: think, scale: 0.5f);
                        var peeked = Run(n, seed * 7 + n, pol, talk: false, previews: true, think: think, scale: 0.5f);
                        Assert.AreEqual(plain.Result.ResultHash, peeked.Result.ResultHash, "think " + think + " n " + n + " seed " + seed);
                    }
        }

        [Test]
        public void Previews_PredictTheRealChoiceAndVote()
        {
            int checkedChoices = 0, checkedVotes = 0;
            foreach (double think in new[] { 0.0, 4.0 })
                for (int n = 3; n <= 6; n++)
                    for (int seed = 1; seed <= 8; seed++)
                    {
                        var setup = new LineSetup { WeekId = 1, MatchId = "peek", Seed = seed * 13 + n };
                        for (int i = 0; i < n; i++) setup.Players.Add(new LinePlayerInfo { Id = i, Name = "B" + i, IsBot = true });
                        var config = LineConfig.CreateDefault();
                        var engine = LineEngineFactory.Create(setup, config);
                        var bots = new LineBot[n];
                        var req = new uint[n];
                        for (int i = 0; i < n; i++) bots[i] = new LineBot(i, seed * 31 + i, Rotation[(i + seed) % 3]) { ThinkSeconds = think };
                        // first preview of each (seat, task) / crisis vote / final round, taken before the bot acts
                        var choice = new Dictionary<string, int>();
                        double now = 0;
                        for (int step = 0; step < 20000 && !engine.IsFinished; step++)
                        {
                            engine.Tick(now);
                            for (int i = 0; i < n; i++)
                            {
                                var v = engine.BuildView(i, now);
                                if (v.Phase == LinePhase.TaskPlay && v.Task != null && !v.Task.Submitted)
                                {
                                    string key = "t" + v.Task.Number + ":" + i;
                                    if (!choice.ContainsKey(key)) choice[key] = bots[i].PlannedTaskChoice(v);
                                }
                                if (v.Phase == LinePhase.CrisisCalls && !choice.ContainsKey("c:" + i)) choice["c:" + i] = bots[i].PlannedVote(v, false, 1);
                                if (v.Phase == LinePhase.FinalVote && v.Final != null && !v.Final.Revote && v.Mine.Ballots.Count > 0 && v.Mine.Ballots[0] < 0)
                                {
                                    string key = "f" + v.Final.Round + ":" + i;
                                    if (!choice.ContainsKey(key)) choice[key] = bots[i].PlannedVote(v, true);
                                }
                                var a = bots[i].Next(v, config, now);
                                if (a != null) engine.Apply(i, new LineAction(a.WeekId, ++req[i], a.Verb, a.Target, a.A, a.B, a.C, a.Text), now);
                            }
                            now += 0.25;
                        }
                        var r = engine.Result;
                        foreach (var c in r.Choices)
                        {
                            int planned;
                            if (!choice.TryGetValue("t" + c.TaskNumber + ":" + c.Player, out planned)) continue;
                            Assert.AreEqual(c.Choice, planned, "task " + c.TaskNumber + " seat " + c.Player + " think " + think);
                            checkedChoices++;
                        }
                        foreach (var b in r.Ballots)
                        {
                            int planned;
                            if (b.Kind == BallotKind.Crisis && choice.TryGetValue("c:" + b.Voter, out planned) && !b.SelfAssigned)
                            { Assert.AreEqual(b.Target, planned, "crisis seat " + b.Voter + " think " + think + " seed " + seed + " n " + n); checkedVotes++; }
                            if (b.Kind == BallotKind.Final && !b.Revote && choice.TryGetValue("f" + b.Round + ":" + b.Voter, out planned) && planned >= 0)
                            { Assert.AreEqual(b.Target, planned, "final seat " + b.Voter); checkedVotes++; }
                        }
                    }
            Assert.Greater(checkedChoices, 100);
            Assert.Greater(checkedVotes, 50);
        }

        // ------------------------------------------------------------------ truth

        [Test]
        public void Talkers_HonestNeverLies_AndClaimsMatchTheResults()
        {
            int lines = 0, honest = 0, checks = 0;
            foreach (var sim in Matches(4, 500))
            {
                lines += sim.Lines.Count;
                foreach (var l in sim.Lines)
                {
                    if (l.Policy == LineBotPolicy.Honest) { honest++; Assert.IsTrue(l.Truthful, "Honest lied: " + l); }
                    if (l.Claim == BotClaimKind.Choice)
                    {
                        var c = sim.Result.Choices.Single(x => x.TaskNumber == l.ClaimTask && x.Player == l.Speaker);
                        Assert.AreEqual(c.Choice == l.ClaimValue, l.Truthful, "choice claim " + l + " by " + l.Speaker + " (" + l.Policy + ")");
                        checks++;
                    }
                    if (l.Claim == BotClaimKind.Vote)
                    {
                        var b = sim.Result.Ballots.FirstOrDefault(x => x.Voter == l.Speaker && x.Kind == l.ClaimBallot && (x.Kind != BallotKind.Final || x.Round == l.ClaimRound && !x.Revote));
                        Assert.IsNotNull(b, "no ballot for " + l);
                        Assert.AreEqual(b.Target == l.ClaimValue, l.Truthful, "vote claim " + l + " by " + l.Speaker + " (" + l.Policy + ")");
                        checks++;
                    }
                }
                Assert.IsFalse(sim.Log.Any(x => x.Contains(" ok=no")), "a claim did not match the results:\n" + string.Join("\n", sim.Log.Where(x => x.Contains(" ok=no"))));
            }
            Assert.Greater(lines, 500, "bots should talk");
            Assert.Greater(honest, 100);
            Assert.Greater(checks, 100);
        }

        [Test]
        public void Greedy_LiesAboutTaking_MixedLiesSometimes()
        {
            int greedyClaims = 0, greedyLies = 0, mixedClaims = 0, mixedLies = 0;
            foreach (var sim in Matches(6, 900))
                foreach (var l in sim.Lines)
                {
                    if (l.Policy == LineBotPolicy.Mixed && l.Claim == BotClaimKind.Vote) { mixedClaims++; if (!l.Truthful) mixedLies++; }
                    if (l.Claim != BotClaimKind.Choice) continue;
                    var c = sim.Result.Choices.Single(x => x.TaskNumber == l.ClaimTask && x.Player == l.Speaker);
                    bool contradicts = c.Choice != l.ClaimValue;
                    if (l.Policy == LineBotPolicy.Greedy)
                    {
                        greedyClaims++;
                        // Greedy takes / skips every time and claims the cooperative choice: a lie each time
                        Assert.IsTrue(contradicts, "Greedy told the truth? " + l);
                        Assert.IsFalse(l.Truthful);
                        greedyLies++;
                    }
                    if (l.Policy == LineBotPolicy.Mixed)
                    {
                        mixedClaims++;
                        if (!l.Truthful) mixedLies++;
                    }
                }
            Assert.Greater(greedyClaims, 20);
            Assert.AreEqual(greedyClaims, greedyLies);
            Assert.Greater(mixedClaims, 10);
            Assert.Greater(mixedLies, 0, "Mixed should lie sometimes");
            Assert.Less(mixedLies, mixedClaims, "Mixed should also tell the truth");
        }

        // ------------------------------------------------------------------ privacy, placeholders, the engine

        [Test]
        public void Lines_NameOnlyWhatTheSpeakerCouldKnow_AndFillEveryPlaceholder()
        {
            int named = 0;
            foreach (var sim in Matches(3, 1300))
            {
                Assert.IsEmpty(sim.Leaks, "a line named a colleague the speaker never saw take");
                foreach (var l in sim.Lines)
                {
                    Assert.IsTrue(LineBotLines.Valid(l), "bad placeholders: " + l);
                    var sig = LineBotLines.Signature(l.Key);
                    Assert.AreEqual(sig.Length, l.Args.Count);
                    for (int i = 0; i < sig.Length; i++)
                        if (sig[i] == 'p')
                        {
                            named++;
                            Assert.IsTrue(sim.Result.Players.Any(p => p.Player == l.Args[i]), "unknown player in " + l);
                            Assert.AreNotEqual(l.Speaker, l.Args[i], "a bot names itself: " + l);
                        }
                    if (l.Text)
                    {
                        string key;
                        List<int> args;
                        Assert.IsTrue(LineBotLines.TryParse(LineBotLines.Token(l), out key, out args), "token " + l);
                        Assert.AreEqual(l.Key, key);
                        CollectionAssert.AreEqual(l.Args, args);
                    }
                }
            }
            Assert.Greater(named, 50);
        }

        [Test]
        public void Talkers_UseTheEngineCleanly_CallAnswerPayAndChat()
        {
            int asks = 0, phone = 0, chat = 0, transfers = 0, calls = 0;
            var allowed = new HashSet<LineFailure> { LineFailure.Busy, LineFailure.NotRinging, LineFailure.AlreadyInCall, LineFailure.NotInCall, LineFailure.RateLimited };
            foreach (var sim in Matches(3, 1700))
            {
                foreach (var w in sim.RejectedWhat)
                    Assert.IsTrue(allowed.Any(f => w.EndsWith(": " + f, StringComparison.Ordinal)), "unexpected rejection: " + w);
                asks += sim.Asks;
                phone += sim.PhoneLines;
                chat += sim.Lines.Count(l => l.Text);
                transfers += sim.Transfers;
                calls += sim.Log.Count(x => x.StartsWith("bottalk call ", StringComparison.Ordinal));
                Assert.IsTrue(sim.Log.Any(x => x.StartsWith("bottalk summary ", StringComparison.Ordinal)), "summary logged at Results");
            }
            Assert.Greater(calls, 30, "bots should call");
            Assert.Greater(phone, 100, "phone lines");
            Assert.Greater(asks, 10, "bot-to-bot questions");
            Assert.Greater(chat, 30, "meeting chat lines");
            Assert.Greater(transfers, 0, "kept promises are paid");
        }

        [Test]
        public void Talk_IsDeterministicPerSeed()
        {
            var a = Run(5, 4242, s => Rotation[s % 3]);
            var b = Run(5, 4242, s => Rotation[s % 3]);
            Assert.AreEqual(a.Lines.Count, b.Lines.Count);
            for (int i = 0; i < a.Lines.Count; i++)
            {
                Assert.AreEqual(a.Lines[i].ToString(), b.Lines[i].ToString());
                Assert.AreEqual(a.Lines[i].Truthful, b.Lines[i].Truthful);
            }
            Assert.AreEqual(a.Result.ResultHash, b.Result.ResultHash);
        }

        [Test]
        public void HumanQuestions_GetAnswers_ForEveryButton()
        {
            // a bot in a call with seat 0 (driven by hand) answers each question of the current task / crisis
            foreach (var set in new[] { BotQuestionSet.ClientBonus, BotQuestionSet.PettyCash, BotQuestionSet.WeekendShift, BotQuestionSet.Crisis })
                foreach (var policy in Rotation)
                {
                    var t = new LineTest(4, 77, c => c.Durations.Scale(0.5f));
                    var bot = new LineBot(1, 5, policy) { ThinkSeconds = 2 };
                    var talker = new LineBotTalker(1, 9, policy, bot);
                    if (set == BotQuestionSet.Crisis) t.Until(LinePhase.CrisisCalls);
                    else
                    {
                        int task = set == BotQuestionSet.ClientBonus ? 0 : set == BotQuestionSet.PettyCash ? 1 : 2;
                        for (int guard = 0; guard < 200 && !(t.Engine.Phase == LinePhase.TaskPlay && t.View(1).Task.Number == task); guard++) t.Next();
                    }
                    var o = new BotTalkOutput();
                    var said = new List<BotLine>();
                    // Mixed may say "Sonra ararım" and decline: ring again (Honest always answers)
                    for (int attempt = 0; attempt < 6 && t.View(1).Mine.Phone.Status != CallStatus.Connected; attempt++)
                    {
                        if (t.View(0).Mine.Phone.Status == CallStatus.Idle) t.Ok(0, LineVerb.Call, 1);
                        for (int step = 0; step < 40 && t.View(1).Mine.Phone.Status == CallStatus.Ringing; step++)
                        {
                            o.Clear();
                            talker.Tick(t.View(1), t.Config, t.Now, null, o);
                            foreach (var a in o.Actions) if (a.Verb == LineVerb.Answer || a.Verb == LineVerb.Decline) t.Ok(1, a.Verb);
                            t.Advance(0.25);
                        }
                        if (policy == LineBotPolicy.Honest) Assert.AreEqual(CallStatus.Connected, t.View(1).Mine.Phone.Status, set + ": Honest always answers");
                    }
                    Assert.AreEqual(CallStatus.Connected, t.View(1).Mine.Phone.Status, set + " " + policy + ": the bot answers in the end");
                    int questions = LineBotLines.Questions(set).Count;
                    for (int q = 0; q < questions; q++)
                    {
                        talker.Hear(new BotAsk { From = 0, To = 1, Set = set, Index = q });
                        for (int step = 0; step < 12; step++)
                        {
                            o.Clear();
                            talker.Tick(t.View(1), t.Config, t.Now, null, o);
                            said.AddRange(o.Lines);
                            t.Advance(0.25);
                        }
                    }
                    var answers = said.Where(l => !l.Key.StartsWith("bot.answer.", StringComparison.Ordinal) && !l.Key.StartsWith("bot.bye.", StringComparison.Ordinal)).ToList();
                    Assert.GreaterOrEqual(answers.Count, questions, set + " " + policy + ": " + string.Join(", ", said));
                    foreach (var l in said)
                    {
                        Assert.IsTrue(LineBotLines.Valid(l), l.ToString());
                        Assert.AreEqual(0, l.Listener);
                        if (policy == LineBotPolicy.Honest) Assert.IsTrue(l.Truthful, l.ToString());
                    }
                }
        }

        [Test]
        public void Catalogue_TokensAndQuestions()
        {
            var l = new BotLine { Key = "bot.meet.victim.h", Speaker = 2 };
            l.Args.Add(3);
            Assert.IsTrue(LineBotLines.Valid(l));
            string key;
            List<int> args;
            Assert.IsTrue(LineBotLines.TryParse(LineBotLines.Token(l), out key, out args));
            Assert.AreEqual("bot.meet.victim.h", key);
            CollectionAssert.AreEqual(new[] { 3 }, args);
            Assert.IsFalse(LineBotLines.TryParse("bot|bot.meet.victim.h|3", out key, out args), "needs the marker");
            Assert.IsFalse(LineBotLines.TryParse(LineBotLines.TokenPrefix + "bot.meet.victim.h", out key, out args), "missing argument");
            Assert.IsFalse(LineBotLines.TryParse(LineBotLines.TokenPrefix + "bot.meet.victim.h|9", out key, out args), "player out of range");
            Assert.IsFalse(LineBotLines.TryParse(LineBotLines.TokenPrefix + "bot.nope|1", out key, out args), "unknown key");
            // the marker survives the engine's text sanitiser
            LineFailure f;
            Assert.AreEqual(LineBotLines.Token(l), LineText.Sanitize(LineBotLines.Token(l), 160, false, out f));
            Assert.AreEqual(3, LineBotLines.Questions(BotQuestionSet.ClientBonus).Count);
            Assert.AreEqual(3, LineBotLines.Questions(BotQuestionSet.PettyCash).Count);
            Assert.AreEqual(2, LineBotLines.Questions(BotQuestionSet.WeekendShift).Count);
            Assert.AreEqual(2, LineBotLines.Questions(BotQuestionSet.Crisis).Count);
            Assert.AreEqual(0, LineBotLines.Questions(BotQuestionSet.None).Count);
            foreach (var k in LineBotLines.Keys) Assert.IsTrue(k.StartsWith("bot.", StringComparison.Ordinal), k);
        }
    }
}
