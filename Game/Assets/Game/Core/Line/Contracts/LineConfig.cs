using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>Phase lengths in seconds (docs/V5_ENGINE_SPEC.md §3). <see cref="Scale"/> multiplies all of them (tests, bots).</summary>
    [Serializable]
    public sealed class LineDurations
    {
        public float Intro = 20f;
        public float TaskPlay = 60f;
        public float TaskReveal = 8f;
        public float Break = 30f;
        public float MeetingDossier = 15f;
        public float MeetingDiscussion = 50f;
        public float MeetingVote = 20f;
        public float MeetingVerdict = 6f;
        public float CrisisCalls = 75f;
        public float CrisisVote = 20f;
        public float CrisisVerdict = 8f;
        public float FinalDiscussion = 40f;
        /// <summary>Per candidate.</summary>
        public float FinalDefense = 15f;
        public float FinalVote = 25f;
        /// <summary>The single revote after a tie.</summary>
        public float FinalRevote = 15f;
        public float FinalVerdict = 10f;
        public float Results = 45f;
        /// <summary>Calls ring this long before they count as missed.</summary>
        public float Ring = 12f;

        public void Scale(float s)
        {
            if (s <= 0f || Math.Abs(s - 1f) < 0.0001f) return;
            Intro *= s; TaskPlay *= s; TaskReveal *= s; Break *= s;
            MeetingDossier *= s; MeetingDiscussion *= s; MeetingVote *= s; MeetingVerdict *= s;
            CrisisCalls *= s; CrisisVote *= s; CrisisVerdict *= s;
            FinalDiscussion *= s; FinalDefense *= s; FinalVote *= s; FinalRevote *= s; FinalVerdict *= s;
            Results *= s; Ring *= s;
        }
    }

    /// <summary>
    /// Every v5 tunable (docs/V5_ENGINE_SPEC.md, prototype numbers v0 from docs/V5_DESIGN.md §12).
    /// prim = money = score; suç = public blame.
    /// </summary>
    [Serializable]
    public sealed class LineConfig
    {
        public int ConfigVersion = 5;
        public LineDurations Durations = new LineDurations();
        /// <summary>When every connected player has submitted, TaskPlay ends this many seconds later.</summary>
        public float EarlyFinishSeconds = 3f;
        /// <summary>Seeded shuffles tried to avoid repeating a task-1 pair in task 2.</summary>
        public int PairingTries = 200;
        /// <summary>Fewer than two connected players for this long ⇒ Abandoned.</summary>
        public float AbandonSeconds = 60f;

        // ---- prim
        public int StartPrim = 6;
        // Client Bonus, pair
        public int BonusPairShare = 2;
        public int BonusPairTaker = 5;
        public int BonusPairSharer = 0;
        public int BonusPairBothTake = 1;
        // Client Bonus, trio
        public int BonusTrioNone = 2;
        public int BonusTrioTaker = 5;
        public int BonusTrioOthers = 0;
        public int BonusTrioMany = 1;
        // Petty Cash
        public int CashTake = 3;
        public int CashCaughtBlame = 2;
        public int CashWhistleBlame = 3;
        public int CashWhistleReward = 2;
        // Weekend Shift
        public int ShiftSkipPay = 2;

        // ---- investigation
        public int InvestigationsPerPlayer = 1;
        public int InvestigationCost = 4;
        public int InvestigationGuiltyBlame = 3;
        public int InvestigationRefund = 3;
        public int InvestigationFalseBlame = 3;

        // ---- meetings, crisis, final
        public int BlamePerVote = 1;
        public int CrisisBlame = 4;
        /// <summary>Final candidates for 3, 4, 5, 6 players (ties with the last candidate are added).</summary>
        public List<int> CandidatesByPlayers = new List<int> { 2, 2, 3, 3 };
        /// <summary>Firings for 3, 4, 5, 6 players.</summary>
        public List<int> FiringsByPlayers = new List<int> { 1, 1, 2, 2 };

        // ---- transfers
        public int TransferMax = 10;
        public float TransferMinInterval = 1f;

        // ---- recorder and leaks
        public bool RecordingsEnabled = true;
        public int RecorderUses = 1;
        public float LeakSeconds = 7f;
        /// <summary>A leak plays no earlier than this long after it was requested.</summary>
        public float LeakDelay = 0.5f;

        // ---- messages
        public int TextMaxChars = 200;
        public int MeetingChatMaxChars = 160;
        public float MessageMinInterval = 1.5f;
        /// <summary>Private texts per segment (tasks + meeting; reset when the meeting index changes).</summary>
        public int TextsPerSegment = 60;
        /// <summary>Public chat lines per segment (reset when the meeting index changes).</summary>
        public int MeetingChatPerSegment = 30;
        /// <summary>Messages the engine keeps per recipient (oldest dropped first).</summary>
        public int MessagesKeptPerPlayer = 200;

        public int CandidatesFor(int players) { return ByPlayers(CandidatesByPlayers, players); }
        public int FiringsFor(int players) { return ByPlayers(FiringsByPlayers, players); }
        /// <summary>Weekend Shift volunteers needed: ceil(N / 2).</summary>
        public static int ShiftNeeded(int players) { return (players + 1) / 2; }

        static int ByPlayers(List<int> values, int players)
        {
            int i = players - LineProtocol.MinPlayers;
            return values != null && i >= 0 && i < values.Count ? values[i] : 0;
        }

        public static LineConfig CreateDefault() { return new LineConfig(); }
    }
}
