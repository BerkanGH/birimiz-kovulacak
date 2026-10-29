using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests.Line
{
    internal sealed class LineTest
    {
        internal readonly ILineEngine Engine;
        internal readonly LineConfig Config;
        internal readonly LineSetup Setup;
        internal double Now;
        private readonly uint[] requests = new uint[6];

        internal static LineSetup MakeSetup(int count = 4, int seed = 731)
        {
            var s = new LineSetup { WeekId = 42, Seed = seed, ManagerProfileId = LineManagerIds.Disiplinci };
            for (int i = 0; i < count; i++) s.Players.Add(new LinePlayerInfo { Id = i, Name = "Seat " + i });
            return s;
        }

        internal LineTest(LineSetup setup = null, Action<LineConfig> configure = null)
        {
            Setup = setup ?? MakeSetup(); Config = LineConfig.CreateDefault();
            // Isolated legacy round fixtures explicitly opt into complaints at smaller tables.
            // Generated schedules and direct factory validation tests retain the production minimum.
            if (Setup.ForceRounds != null && Setup.ForceRounds.Contains(RoundType.IhbarKutusu)) Config.Rounds.ComplaintMinPlayers = 3;
            configure?.Invoke(Config); Now = Setup.StartTime;
            Engine = LineEngineFactory.Create(Setup, Config);
        }

        internal LineView View(int player = 0) { return Engine.BuildView(player, Now); }
        internal LinePlayerView Player(int player) { return View().Players.Single(p => p.Id == player); }
        internal LineActionResult Do(int player, LineVerb verb, int target = -1, int a = -1, int b = -1)
        {
            return Engine.Apply(player, new LineAction(Setup.WeekId, ++requests[player], verb, target, a, b), Now);
        }
        internal void Ok(int player, LineVerb verb, int target = -1, int a = -1, int b = -1)
        {
            var r = Do(player, verb, target, a, b); Assert.IsTrue(r.Accepted, verb + ": " + r);
        }
        internal void Advance(double seconds) { Now += seconds; Engine.Tick(Now); }
        internal void Next() { Now = Engine.PhaseEndTime; Engine.Tick(Now); }
        internal void Until(LinePhase phase, int day = -1, int round = -1)
        {
            for (int guard = 0; guard < 200; guard++)
            {
                var v = View();
                if (v.Phase == phase && (day < 0 || day == v.Day) && (round < 0 || round == v.RoundIndex)) return;
                Assert.IsFalse(Engine.IsFinished, "Finished before " + phase); Next();
            }
            Assert.Fail("Never reached " + phase);
        }
        internal LineRoundReveal Reveal() { Until(LinePhase.RoundReveal); return View().Round.Reveal; }
        internal void Finish() { while (!Engine.IsFinished) Next(); }

        // A prior all-take round creates enough blame to observe relief rather than only its zero floor.
        internal static LineTest Primed(RoundType round, string manager = LineManagerIds.Disiplinci, Action<LineConfig> configure = null)
        {
            var s = MakeSetup(); s.ManagerProfileId = manager;
            s.ForceRounds = new List<RoundType> { RoundType.PrimHavuzu, round, RoundType.PrimHavuzu, RoundType.IhbarKutusu, RoundType.TerfiListesi, RoundType.OrtakSifre };
            s.ForceHidden = new List<int> { 0, 0, 0, 0, 0, 0 };
            var m = new LineTest(s, c => { c.StartBlame = 0; c.Rounds.PrimBothTake = 10; configure?.Invoke(c); });
            m.Until(LinePhase.RoundPlay);
            for (int i = 0; i < 4; i++) m.Ok(i, LineVerb.SubmitChoice, a: 1);
            m.Until(LinePhase.RoundPlay, 0, 1);
            return m;
        }

        internal static LineTest WithTrick(TrickType trick, Action<LineConfig> configure = null, int count = 4)
        {
            var s = MakeSetup(count); s.History = new LineSessionHistory { WeeksPlayed = 1 };
            s.ForceTricks = new List<TrickType>();
            s.ForceTricks.Add(trick);
            for (int i = 1; i < count; i++)
            {
                TrickType t = (TrickType)i;
                if (t == trick) t = (TrickType)count;
                s.ForceTricks.Add(t);
            }
            s.ForceRounds = new List<RoundType> { RoundType.IhbarKutusu, RoundType.TerfiListesi, RoundType.PrimHavuzu, RoundType.IhbarKutusu, RoundType.TerfiListesi, RoundType.OrtakSifre };
            var m = new LineTest(s, c => { c.Profiles.ForEach(p => { p.TraceChance = 0; p.LeakChance = 0; }); configure?.Invoke(c); });
            m.Until(LinePhase.RoundPlay); return m;
        }
    }
}
