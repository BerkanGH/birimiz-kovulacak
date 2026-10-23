using System.Collections.Generic;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Game
{
    /// <summary>Named test scenarios that force roles/objectives so scripted bots can reproduce a story.</summary>
    public static class ScenarioSetup
    {
        public const string RubberDuck = "duck";

        public static void Apply(string scenario, MatchSetup setup)
        {
            if (string.IsNullOrEmpty(scenario)) return;
            if (scenario == RubberDuck)
            {
                // Mavi (0) is framed, Sarı (1) swaps nameplates, Pembe (2) plants the duck, Yeşil (3) is the Informant.
                setup.ForceInformant = 3;
                setup.ForceObjectives = new List<string> { "PROTECTION_DEAL", "CHAOS_COORDINATOR", "SCAPEGOAT", "" };
                setup.ForceTargets = new List<int> { 3, PlayerIds.None, 0, PlayerIds.None };
            }
        }
    }
}
