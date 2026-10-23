using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Lightweight "today's conditions" for rematch variety. Each modifier only tweaks a per-match copy of the
    /// MatchConfig before the engine is created (no rule changes). The first match of a session is always normal.
    /// </summary>
    public static class DayModifiers
    {
        public sealed class Modifier
        {
            public string Id;
            public int Weight;
            public Action<MatchConfig> Apply;
        }

        public const string Normal = "day.normal";

        public static readonly List<Modifier> All = new List<Modifier>
        {
            new Modifier { Id = Normal, Weight = 4, Apply = c => { } },
            new Modifier { Id = "day.coffee_crisis", Weight = 2, Apply = c => { c.Tasks.CoffeeOrderInterval *= 0.55f; c.Tasks.MaxCoffeeOrders += 1; } },
            new Modifier { Id = "day.printer_moody", Weight = 2, Apply = c => { c.Tasks.PrintSeconds = 10f; c.Tasks.ReportPoints += 1f; } },
            new Modifier { Id = "day.eagle_eye", Weight = 2, Apply = c => { c.Manager.VisionHalfAngle = 85f; c.Manager.VisionRange = 12f; } },
            new Modifier { Id = "day.phone_storm", Weight = 2, Apply = c => { c.Tasks.PhoneIntervalMin = 16f; c.Tasks.PhoneIntervalMax = 26f; } },
            new Modifier { Id = "day.audit_day", Weight = 1, Apply = c => { c.Informant.AuditScans += 1; } },
        };

        public static Modifier Pick(int matchesCompleted, int seed)
        {
            if (matchesCompleted == 0) return All[0];
            int total = 0;
            foreach (var m in All) total += m.Weight;
            int roll = new Random(seed).Next(total);
            foreach (var m in All)
            {
                roll -= m.Weight;
                if (roll < 0) return m;
            }
            return All[0];
        }
    }
}
