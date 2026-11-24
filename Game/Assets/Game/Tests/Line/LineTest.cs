using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests.Line
{
    /// <summary>Test harness over the public engine API (no internals).</summary>
    internal sealed class LineTest
    {
        internal readonly ILineEngine Engine;
        internal readonly LineSetup Setup;
        internal readonly LineConfig Config;
        internal double Now;
        internal readonly List<LineEvent> Events = new List<LineEvent>();
        private readonly uint[] requests = new uint[6];

        internal static LineSetup MakeSetup(int count = 4, int seed = 731)
        {
            var s = new LineSetup { WeekId = 42, Seed = seed, MatchId = "fixture-match" };
            for (int i = 0; i < count; i++) s.Players.Add(new LinePlayerInfo { Id = i, Name = "Seat " + i, ColorIndex = i });
            return s;
        }

        internal LineTest(int count = 4, int seed = 731, Action<LineConfig> configure = null, Action<LineSetup> prepare = null)
        {
            Setup = MakeSetup(count, seed);
            prepare?.Invoke(Setup);
            Config = LineConfig.CreateDefault();
            configure?.Invoke(Config);
            Now = Setup.StartTime;
            Engine = LineEngineFactory.Create(Setup, Config);
        }

        internal IEnumerable<int> Ids { get { return Setup.Players.Select(p => p.Id); } }
        internal LineView View(int player = 0) { return Engine.BuildView(player, Now); }
        internal LinePlayerView Player(int id) { return View().Players.Single(p => p.Id == id); }
        internal int Prim(int id) { return View(id).Mine.Prim; }
        internal int Blame(int id) { return Player(id).Blame; }
        internal List<int> Group(int id) { return View(id).Mine.Group; }

        internal LineActionResult Do(int player, LineVerb verb, int target = -1, int a = -1, string text = "")
        {
            var r = Engine.Apply(player, new LineAction(Setup.WeekId, ++requests[player], verb, target, a, -1, -1, text), Now);
            Drain();
            return r;
        }

        /// <summary>Any action with all of its fields (pranks use B and C).</summary>
        internal LineActionResult DoFull(int player, LineVerb verb, int target, int a, int b, int c, string text = "")
        {
            var r = Engine.Apply(player, new LineAction(Setup.WeekId, ++requests[player], verb, target, a, b, c, text), Now);
            Drain();
            return r;
        }

        internal void Ok(int player, LineVerb verb, int target = -1, int a = -1, string text = "")
        {
            var r = Do(player, verb, target, a, text);
            Assert.IsTrue(r.Accepted, verb + " by " + player + ": " + r);
        }

        internal void Fail(LineFailure expected, int player, LineVerb verb, int target = -1, int a = -1, string text = "")
        {
            var r = Do(player, verb, target, a, text);
            Assert.IsFalse(r.Accepted, verb + " by " + player + " should fail");
            Assert.AreEqual(expected, r.Failure, verb + " by " + player);
        }

        internal void Drain() { Events.AddRange(Engine.DrainEvents()); }
        internal void Advance(double seconds) { Now += seconds; Engine.Tick(Now); Drain(); }
        internal void Next() { Now = Engine.PhaseEndTime; Engine.Tick(Now); Drain(); }

        internal void Until(LinePhase phase, int day = -1)
        {
            for (int i = 0; i < 200; i++)
            {
                if (Engine.Phase == phase && (day < 0 || Engine.Day == day)) return;
                Assert.IsFalse(Engine.IsFinished, "Finished before " + phase);
                Next();
            }
            Assert.Fail("Never reached " + phase);
        }

        internal void Finish() { while (!Engine.IsFinished) Next(); }

        /// <summary>Runs to the TaskPlay of task <paramref name="number"/>, submits <paramref name="choice"/> for everyone and reveals.</summary>
        internal void PlayTask(int number, Func<int, int> choice)
        {
            for (int i = 0; i < 200 && !(Engine.Phase == LinePhase.TaskPlay && View().TaskNumber == number); i++) Next();
            Assert.AreEqual(LinePhase.TaskPlay, Engine.Phase);
            foreach (int id in Ids) Ok(id, LineVerb.SubmitTask, a: choice(id));
            Until(LinePhase.TaskReveal);
        }

        internal static int Share(int id) { return LineChoice.Share; }

        /// <summary>Plays tasks 1 and 2 with the given choices and stops at meeting 1's dossier.</summary>
        internal void ToMeeting1(Func<int, int> bonus = null, Func<int, int> cash = null)
        {
            PlayTask(0, bonus ?? Share);
            PlayTask(1, cash ?? Share);
            Until(LinePhase.MeetingDossier, 0);
        }

        /// <summary>Clean tasks 1-2, task 3 with the given volunteers, then straight to the crisis calls (v5.2). With
        /// <see cref="LineConfig.ShiftMeeting"/> a failed task 3 holds meeting 2 first: with
        /// <see cref="LineConfig.ShiftFailVote"/> (v5.0, <see cref="Meeting2Vote"/>) the given single ballots; v5.1 has no
        /// meeting 2 vote (the skippers' suç is automatic).</summary>
        internal void ToCrisis(Func<int, int> volunteer = null, Func<int, int> meeting2Vote = null)
        {
            ToMeeting1();
            PlayTask(2, volunteer ?? (id => LineChoice.Skip));
            Next();
            if (Engine.Phase == LinePhase.MeetingDossier)
            {
                for (int i = 0; i < 20 && Engine.Phase != LinePhase.MeetingVote && Engine.Phase != LinePhase.CrisisCalls; i++) Next();
                if (Engine.Phase == LinePhase.MeetingVote && meeting2Vote != null) foreach (int id in Ids) Ok(id, LineVerb.Vote, meeting2Vote(id), 0);
                Until(LinePhase.CrisisCalls);
            }
            Assert.AreEqual(LinePhase.CrisisCalls, Engine.Phase);
        }

        /// <summary>The v5.0 Weekend Shift (and Client Bonus share) numbers: an unstaffed shift pays the skippers 2 and is
        /// followed by meeting 2 (<see cref="LineConfig.ShiftMeeting"/>), which holds a suç vote (no automatic suç). Tests of
        /// the crisis and the final use it to set suç up with ballots.</summary>
        internal static void Meeting2Vote(LineConfig c)
        {
            c.ShiftMeeting = true;
            c.ShiftFailVote = true;
            c.ShiftFailSkipBlame = 0;
            c.ShiftFailSkipPay = c.ShiftSkipPay;
            c.BonusPairShare = 2;
            c.BonusTrioNone = 2;
        }

        /// <summary>The v5.1 flow: an unstaffed shift is followed by meeting 2 (dossier and discussion, no vote), where
        /// the skippers get their suç; no prim declarations.</summary>
        internal static void V51(LineConfig c)
        {
            c.ShiftMeeting = true;
            c.DeclarationsEnabled = false;
        }

        /// <summary>Every number and switch of the v5.0 rules (LineSim --rules old).</summary>
        internal static void V50(LineConfig c)
        {
            Meeting2Vote(c);
            c.DeclarationsEnabled = false;
            c.CashWhistleReward = 2;
            c.CashWhistledKeeps = false;
            c.ShiftVolunteerClean = 0;
            c.InvestigationSharerOnly = false;
            c.InvestigationCost = 4;
            c.InvestigationGuiltyBlame = 3;
            c.InvestigationRefund = 3;
            c.CandidateMinBlame = 0;
        }

        internal List<LinePhase> PhaseTrace()
        {
            return Events.Where(e => e.Type == LineEventType.PhaseChanged).Select(e => (LinePhase)e.A).ToList();
        }
    }
}
