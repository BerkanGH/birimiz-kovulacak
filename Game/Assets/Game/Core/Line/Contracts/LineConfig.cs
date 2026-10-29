using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>Phase lengths in seconds (docs/V3_DESIGN.md §1). <see cref="Scale"/> multiplies all of them (tests, bots).</summary>
    [Serializable]
    public sealed class LineDurations
    {
        public float Morning = 20f;
        /// <summary>The first day of a session (onboarding).</summary>
        public float MorningFirst = 40f;
        public float RoundIntro = 7f;
        /// <summary>A round type that has not appeared yet in this session.</summary>
        public float RoundIntroFirst = 16f;
        public float RoundPlay = 150f;
        /// <summary>When every connected player has submitted, RoundPlay ends at most this much later.</summary>
        public float AllSubmittedRemaining = 10f;
        public float RoundReveal = 12f;
        public float Dossier = 25f;
        /// <summary>Added to the dossier per recording (snippet + gap).</summary>
        public float DossierPerRecording = 9f;
        public float Discussion = 75f;
        public float FinalDiscussion = 60f;
        public float Vote = 25f;
        public float Verdict = 8f;
        public float DefensePerPlayer = 30f;
        public float FinalVote = 30f;
        public float FinalVerdict = 12f;
        public float WeekResults = 45f;
        /// <summary>Calls ring this long before they count as missed.</summary>
        public float Ring = 15f;

        public void Scale(float s)
        {
            if (s <= 0f || Math.Abs(s - 1f) < 0.0001f) return;
            Morning *= s; MorningFirst *= s; RoundIntro *= s; RoundIntroFirst *= s; RoundPlay *= s; AllSubmittedRemaining *= s;
            RoundReveal *= s; Dossier *= s; DossierPerRecording *= s; Discussion *= s; FinalDiscussion *= s; Vote *= s;
            Verdict *= s; DefensePerPlayer *= s; FinalVote *= s; FinalVerdict *= s; WeekResults *= s; Ring *= s;
        }
    }

    /// <summary>Blame deltas of every round (docs/V3_DESIGN.md §3). Negative = good for the player.</summary>
    [Serializable]
    public sealed class LineRoundRules
    {
        // Bonus Pool
        public int PrimBothShare = -1;
        public int PrimTakerWins = -4;
        public int PrimSharerLoses = 2;
        public int PrimBothTake = 1;
        /// <summary>v3.0 only (v3.1 uses a trio instead of the intern).</summary>
        public float InternShareChance = 0.6f;
        // v3.1 trio (odd player count): nobody takes / one taker / two takers / all take
        public int PrimTrioNobodyTakes = -1;
        public int PrimTrioSoloTaker = -4;
        public int PrimTrioPairTakers = -2;
        public int PrimTrioSharer = 2;
        public int PrimTrioAllTake = 1;
        // Odd Email / Lost Report (same shape: hidden player vs. plurality vote)
        public int HiddenCaught = 3;
        public int OddCaughtButGuessed = 1;
        public int CorrectVoter = -1;
        public int HiddenEscaped = -2;
        public int WrongInnocent = 2;
        public int OddGuessOptions = 4;
        public int KayipRaporMinPlayers = 4;
        // Complaint Box
        public int ComplaintTop = 2;
        public int ComplaintNone = -1;
        public int ComplaintReasons = 6;
        /// <summary>v3.1: the Complaint Box is only scheduled with at least this many players.</summary>
        public int ComplaintMinPlayers = 5;
        // Promotion List
        public int PromotionWinner = -3;
        public int PromotionBacker = -1;
        // Shared Password
        public int PasswordCracked = -1;
        public int PasswordCrackedSaboteur = 2;
        public int PasswordFailed = 1;
        public int PasswordFailedSaboteur = -2;
        /// <summary>v3.0 only (v3.1: one code per player, cracked when more than half entered the exact code).</summary>
        public int AttemptsPerPlayer = 2;
    }

    /// <summary>A manager personality (docs/V3_DESIGN.md §7). Names/texts come from localisation: manager.&lt;id&gt;.*</summary>
    [Serializable]
    public sealed class LineProfile
    {
        public string Id = "";
        public int IhtarBlame = 3;
        /// <summary>v3.0 only (v3.1 final = shortlist vote).</summary>
        public int FinalVoteWeight = 2;
        /// <summary>Extra (negative) blame for a Bonus Pool taker whose partner shared.</summary>
        public int GreedBonus = 0;
        public float TraceChance = 0.35f;
        public int TrickCaughtBlame = 2;
        public float LeakChance = 0.25f;
        public int Recordings = 1;
        public bool HasFavourite;
    }

    /// <summary>All v3 tunables. Host sends its copy to clients on welcome (JSON).</summary>
    [Serializable]
    public sealed class LineConfig
    {
        public int ConfigVersion = LineProtocol.Version;
        public LineDurations Durations = new LineDurations();
        public LineRoundRules Rounds = new LineRoundRules();
        public int RoundsPerDay = 2;
        public int ShortWeekDays = 3;
        public int FullWeekDays = 5;

        // ---- v3.1 (design review)
        /// <summary>Everyone starts the week with this blame (rewards are negative deltas; the floor stays 0).</summary>
        public int StartBlame = 5;
        /// <summary>The session points leader (strict) starts the week with this much extra blame.</summary>
        public int LeaderStartBonus = 2;
        public int MaxIhtarsPerPlayerPerWeek = 1;
        /// <summary>Final shortlist size by head count: ≤ 4 players / ≥ 5 players.</summary>
        public int FinalShortlistSmall = 2;
        public int FinalShortlistLarge = 3;
        /// <summary>RoundPlay seconds for 3, 4, 5, 6 players (replaces Durations.RoundPlay; scaled like every duration).</summary>
        public List<float> RoundPlayByPlayers = new List<float> { 100f, 120f, 135f, 150f };
        public int TricksPerWeekShort = 1;
        public int TricksPerWeekFull = 2;
        public int TipsPerWeek = 1;
        /// <summary>First day (0-based) the manager line works in the first week of a session (tricks: UnlockDayFirstWeek).</summary>
        public int TipUnlockDayFirstWeek = 2;
        /// <summary>Odd Email topics: categories × items; text key topic.&lt;category*TopicsPerCategory+item&gt;.</summary>
        public int TopicCategories = 6;
        public int TopicsPerCategory = 4;
        /// <summary>Hidden agenda: points per İhtar the target receives (plus AgendaPoints if fired).</summary>
        public int AgendaPointsPerIhtar = 1;
        /// <summary>Recording pick weight multiplier for calls between today's Bonus Pool partners or involving a round's hidden player.</summary>
        public float RecordingFocusWeight = 3f;

        // week points
        public int SurvivePoints = 3;
        public int AgendaPoints = 2;
        public int EmployeeOfWeekPoints = 1;

        // tricks, tips, mail
        /// <summary>First day (0-based) on which tricks and the manager line work in the first week of a session; later weeks: day 0.</summary>
        public int UnlockDayFirstWeek = 1;
        public float CoffeeSeconds = 40f;
        public float PhoneDeadSeconds = 45f;
        public float InternetSeconds = 30f;
        public float WiretapSeconds = 60f;
        public int FalseTipBlame = 2;
        public int ConfirmedTipBlame = 2;
        public int MailTemplates = 8;
        public int MailsPerDay = 1;
        /// <summary>Odd Email topic pairs: ids 0..TopicPairs-1, texts topic.&lt;id&gt;.a / topic.&lt;id&gt;.b.</summary>
        public int TopicPairs = 20;

        // recordings
        public bool RecordingsEnabled = true;
        public float RecordingMinCallSeconds = 6f;
        public float RecordingSnippetSeconds = 7f;
        public int RecordingCandidates = 3;

        public List<LineProfile> Profiles = new List<LineProfile>();

        public LineProfile Profile(string id)
        {
            foreach (var p in Profiles) if (p.Id == id) return p;
            return Profiles.Count > 0 ? Profiles[0] : new LineProfile { Id = id ?? "" };
        }

        public int DaysFor(WeekLength length) { return length == WeekLength.Full ? FullWeekDays : ShortWeekDays; }

        public static LineConfig CreateDefault()
        {
            var c = new LineConfig();
            c.Profiles.Add(new LineProfile { Id = LineManagerIds.Paragoz, IhtarBlame = 2, FinalVoteWeight = 1, GreedBonus = 1 });
            c.Profiles.Add(new LineProfile { Id = LineManagerIds.Disiplinci, IhtarBlame = 4, TraceChance = 0.6f, TrickCaughtBlame = 3 });
            c.Profiles.Add(new LineProfile { Id = LineManagerIds.Kayirmaci, HasFavourite = true });
            c.Profiles.Add(new LineProfile { Id = LineManagerIds.Paranoyak, Recordings = 2, LeakChance = 0.6f });
            return c;
        }
    }
}
