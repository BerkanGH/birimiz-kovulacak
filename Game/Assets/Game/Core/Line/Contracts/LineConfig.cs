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

        /// <summary>The lobby's talk-time option (<see cref="LineTalkTime"/>): only the phases people talk in.</summary>
        public void ScaleTalk(float s)
        {
            if (s <= 0f || Math.Abs(s - 1f) < 0.0001f) return;
            MeetingDiscussion *= s; CrisisCalls *= s; FinalDiscussion *= s; FinalDefense *= s;
        }
    }

    /// <summary>
    /// Every v5 tunable (docs/V5_ENGINE_SPEC.md, numbers from docs/V5_DESIGN.md §12).
    /// prim = money = score; suç = public blame. The defaults are the v5.2 rules (2026-11-24: prim declarations, no
    /// meeting 2) on top of v5.1 (2026-11-15); the comments name the v5.0 value of every number that changed, so a config
    /// can still play the old rules (LineSim --rules old).
    /// </summary>
    [Serializable]
    public sealed class LineConfig
    {
        /// <summary>Schema version of this class (6: Yaramazlıklar added the prank numbers; 7: the v5.1 rules: free
        /// sharer-only investigation, the whistled thief keeps the cash, shift cleaning and the failed shift's automatic suç,
        /// candidates need suç; 8: v5.2: ShiftMeeting, the prim declarations, the talk-time factors). Config files of another
        /// version are ignored.</summary>
        public const int CurrentVersion = 8;
        public int ConfigVersion = CurrentVersion;
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
        /// <summary>Both share (v5.0: 2).</summary>
        public int BonusPairShare = 3;
        public int BonusPairTaker = 5;
        public int BonusPairSharer = 0;
        public int BonusPairBothTake = 1;
        // Client Bonus, trio
        /// <summary>Nobody takes (v5.0: 2).</summary>
        public int BonusTrioNone = 3;
        public int BonusTrioTaker = 5;
        public int BonusTrioOthers = 0;
        public int BonusTrioMany = 1;
        // Petty Cash
        public int CashTake = 3;
        public int CashCaughtBlame = 2;
        public int CashWhistleBlame = 3;
        /// <summary>Prim the (first) whistler earns (v5.0: 2).</summary>
        public int CashWhistleReward = 0;
        /// <summary>A whistled thief keeps the stolen prim (v5.0: false: it was taken back and left the game).</summary>
        public bool CashWhistledKeeps = true;
        // Weekend Shift
        /// <summary>Prim of each skipper when the shift is staffed (volunteers ≥ needed).</summary>
        public int ShiftSkipPay = 2;
        /// <summary>Prim of each skipper when the shift is not staffed (v5.0: 2, like <see cref="ShiftSkipPay"/>).</summary>
        public int ShiftFailSkipPay = 0;
        /// <summary>Suç every skipper of an unstaffed shift gets (v5.0: 0): at the shift's reveal (v5.2), or when meeting 2
        /// opens with <see cref="ShiftMeeting"/>.</summary>
        public int ShiftFailSkipBlame = 2;
        /// <summary>An unstaffed shift is followed by meeting 2 (v5.0 and v5.1: true). v5.2: false, the match goes from the
        /// shift's reveal straight to the crisis calls and every consequence of the shift lands at the reveal.</summary>
        public bool ShiftMeeting = false;
        /// <summary>Meeting 2 holds a suç vote for the unstaffed shift (v5.0: true; needs <see cref="ShiftMeeting"/>).
        /// Without it meeting 2 is the dossier and the discussion only.</summary>
        public bool ShiftFailVote = false;
        /// <summary>Suç removed from every volunteer (never below 0) when the shift resolves, staffed or not (v5.0: 0).</summary>
        public int ShiftVolunteerClean = 1;

        // ---- investigation
        public int InvestigationsPerPlayer = 1;
        /// <summary>Only a Client Bonus sharer may investigate, and only a group-mate who took: no false investigations
        /// (v5.0: false: any group-mate; investigating a sharer blamed the investigator).</summary>
        public bool InvestigationSharerOnly = true;
        /// <summary>Prim the investigator pays (v5.0: 4).</summary>
        public int InvestigationCost = 0;
        /// <summary>Suç of a target who took (v5.0: 3).</summary>
        public int InvestigationGuiltyBlame = 4;
        /// <summary>Prim a guilty target pays the investigator (v5.0: 3).</summary>
        public int InvestigationRefund = 0;
        /// <summary>Suç of an investigator whose target shared (only without <see cref="InvestigationSharerOnly"/>).</summary>
        public int InvestigationFalseBlame = 3;

        // ---- meetings, crisis, final
        public int BlamePerVote = 1;
        public int CrisisBlame = 4;
        /// <summary>Suç a player needs to be a final candidate (v5.0: 0). Fewer qualify than the candidate count: those;
        /// the firings never exceed the candidates; nobody qualifies: nobody is fired.</summary>
        public int CandidateMinBlame = 1;
        /// <summary>Final candidates for 3, 4, 5, 6 players (ties with the last candidate are added).</summary>
        public List<int> CandidatesByPlayers = new List<int> { 2, 2, 3, 3 };
        /// <summary>Firings for 3, 4, 5, 6 players.</summary>
        public List<int> FiringsByPlayers = new List<int> { 1, 1, 2, 2 };
        /// <summary>Prim declarations ("prim beyanı", v5.2): every final candidate may state once how much prim they have
        /// (they may lie); sealed until all are revealed together; stamped against the desk when a candidate is fired.
        /// No effect on prim, suç or votes (v5.1: false).</summary>
        public bool DeclarationsEnabled = true;
        /// <summary>Declaring stays open this long into the final discussion (it opens when the crisis verdict starts);
        /// the declarations are revealed then at the latest.</summary>
        public float DeclareSeconds = 12f;
        /// <summary>Highest amount a candidate may declare.</summary>
        public int DeclareMax = 99;

        // ---- lobby option "Konuşma süresi" (LineSetup.TalkTime): factors on the talking phases
        public float TalkTimeShort = 0.7f;
        public float TalkTimeLong = 1.5f;

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

        // ---- Yaramazlıklar: meeting room pranks and the manager's patience (docs/V5_YARAMAZLIKLAR.md)
        public bool PranksEnabled = true;
        /// <summary>A prank that brings the patience to this or above is a tutanak (+<see cref="TutanakBlame"/> suç) and resets it to 0.</summary>
        public int PatienceCapacity = 6;
        /// <summary>Patience costs are multiplied during the final defence ("Arkadaşınız konuşuyor!"); flattering is not.</summary>
        public int DefencePatienceMultiplier = 2;
        public int TutanakBlame = 1;
        public int CoffeeCost = 2;
        public int CoffeePatience = 3;
        public int CoffeePerSession = 1;
        public int PlaneCost = 1;
        public int PlanePatience = 2;
        public int PlanePerSession = 1;
        public int PlaneNoteMaxChars = 60;
        public int PostItCost = 1;
        public int PostItPatience = 2;
        public int PostItPerSession = 1;
        /// <summary>Phone ring: the patience is charged to the target (who also gets the tutanak).</summary>
        public int PhoneRingCost = 2;
        public int PhoneRingPatience = 3;
        public int PhoneRingPerSession = 1;
        public int SlurpPatience = 1;
        public float SlurpCooldown = 10f;
        public int StandUpPatience = 1;
        public float StandUpCooldown = 10f;
        public int FlatterCost = 1;
        /// <summary>Flattering lowers the patience by this much (never below 0).</summary>
        public int FlatterRelief = 2;
        public int FlatterPerSession = 1;
        public float CakeCooldown = 3f;
        /// <summary>Cakes per player (Results only).</summary>
        public int CakeMax = 5;

        /// <summary>Prim a prank burns (0 = free).</summary>
        public int PrankCost(PrankKind k)
        {
            switch (k)
            {
                case PrankKind.Coffee: return CoffeeCost;
                case PrankKind.Plane: return PlaneCost;
                case PrankKind.PostIt: return PostItCost;
                case PrankKind.PhoneRing: return PhoneRingCost;
                case PrankKind.Flatter: return FlatterCost;
                default: return 0;
            }
        }

        /// <summary>Patience a prank costs outside the defence (Flatter: 0, see <see cref="FlatterRelief"/>; Cake: 0).</summary>
        public int PrankPatience(PrankKind k)
        {
            switch (k)
            {
                case PrankKind.Coffee: return CoffeePatience;
                case PrankKind.Plane: return PlanePatience;
                case PrankKind.PostIt: return PostItPatience;
                case PrankKind.PhoneRing: return PhoneRingPatience;
                case PrankKind.Slurp: return SlurpPatience;
                case PrankKind.StandUp: return StandUpPatience;
                default: return 0;
            }
        }

        /// <summary>Uses per session (0 = no limit; Cake: <see cref="CakeMax"/> per match instead).</summary>
        public int PrankPerSession(PrankKind k)
        {
            switch (k)
            {
                case PrankKind.Coffee: return CoffeePerSession;
                case PrankKind.Plane: return PlanePerSession;
                case PrankKind.PostIt: return PostItPerSession;
                case PrankKind.PhoneRing: return PhoneRingPerSession;
                case PrankKind.Flatter: return FlatterPerSession;
                default: return 0;
            }
        }

        /// <summary>Seconds between two uses of the same prank by one player (0 = none).</summary>
        public float PrankCooldown(PrankKind k)
        {
            switch (k)
            {
                case PrankKind.Slurp: return SlurpCooldown;
                case PrankKind.StandUp: return StandUpCooldown;
                case PrankKind.Cake: return CakeCooldown;
                default: return 0f;
            }
        }

        /// <summary>The factor of a <see cref="LineTalkTime"/> level on MeetingDiscussion, CrisisCalls, FinalDiscussion and
        /// FinalDefense (Normal and anything unknown: 1).</summary>
        public float TalkFactor(int level)
        {
            return level == LineTalkTime.Short ? TalkTimeShort : level == LineTalkTime.Long ? TalkTimeLong : 1f;
        }

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
