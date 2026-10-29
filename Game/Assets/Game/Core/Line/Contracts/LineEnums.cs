using System;

namespace BirimizKovulacak.Core
{
    // v3 "Ofis Hattı" contract (normative rules: docs/V3_DESIGN.md, wire/engine details: docs/CONTRACTS_V3.md).
    // Type names are distinct from v2 so both compile side by side until v2 is removed.

    public static class LineProtocol
    {
        public const int Version = 3;
        public const int MinPlayers = 3;
        public const int MaxPlayers = 6;
    }

    /// <summary>Special participant ids (player ids are 0..5).</summary>
    public static class LineIds
    {
        public const int None = -1;
        /// <summary>NPC intern: the odd player's Bonus Pool partner.</summary>
        public const int Intern = 100;
        /// <summary>The manager (morning call, manager line, mail sender).</summary>
        public const int Manager = 101;
        /// <summary>Company IT / HR system (system mail sender).</summary>
        public const int System = 102;
    }

    public static class LineManagerIds
    {
        public const string Paragoz = "paragoz";
        public const string Disiplinci = "disiplinci";
        public const string Kayirmaci = "kayirmaci";
        public const string Paranoyak = "paranoyak";
        public static readonly string[] All = { Paragoz, Disiplinci, Kayirmaci, Paranoyak };
    }

    public enum LinePhase : byte
    {
        None = 0,
        Morning = 1,
        RoundIntro = 2,
        RoundPlay = 3,
        RoundReveal = 4,
        MeetingDossier = 5,
        MeetingDiscussion = 6,
        MeetingVote = 7,
        MeetingVerdict = 8,
        FinalDefense = 9,
        FinalVote = 10,
        FinalVerdict = 11,
        WeekResults = 12,
        Abandoned = 13,
    }

    public enum WeekLength : byte { Short = 0, Full = 1 }

    public enum RoundType : byte
    {
        None = 0,
        PrimHavuzu = 1,
        FarkliEposta = 2,
        KayipRapor = 3,
        IhbarKutusu = 4,
        TerfiListesi = 5,
        OrtakSifre = 6,
    }

    public enum TrickType : byte
    {
        None = 0,
        SahteEposta = 1,
        KahveDok = 2,
        FisCek = 3,
        InternetKes = 4,
        HatDinle = 5,
        /// <summary>Retired in v3.1 (it deleted the meeting's best moment); never dealt.</summary>
        KayitSil = 6,
    }

    /// <summary>The local player's phone. Dialing = I am calling and it rings at the other end; Ringing = somebody calls me.</summary>
    public enum CallStatus : byte { Idle = 0, Dialing = 1, Ringing = 2, Connected = 3 }

    public enum CallEndReason : byte
    {
        None = 0,
        HungUp = 1,
        Declined = 2,
        Missed = 3,
        /// <summary>A Pull-the-Plug trick cut one side.</summary>
        LineCut = 4,
        /// <summary>A meeting or the end of the day ended every call.</summary>
        PhaseEnd = 5,
        Disconnected = 6,
    }

    public enum LineVerb : byte
    {
        None = 0,
        /// <summary>Target = callee.</summary>
        Call = 1,
        Answer = 2,
        Decline = 3,
        HangUp = 4,
        /// <summary>Round choice; meaning of Target/A per round in CONTRACTS_V3 §3. Can be changed until RoundPlay ends.</summary>
        SubmitChoice = 5,
        /// <summary>Shared Password attempt: A = code packed as a base-10 integer of exactly CodeLength digits (leading zeros allowed).</summary>
        Attempt = 6,
        /// <summary>Use this week's dirty trick: Target = victim (or apparent sender for a fake email), A = mail template.</summary>
        UseTrick = 7,
        /// <summary>Manager line: Target = the colleague you claim used a dirty trick today.</summary>
        TipOff = 8,
        /// <summary>Meeting / final vote: Target or -1 to abstain. Can be changed until the phase ends.</summary>
        Vote = 9,
        /// <summary>Genuine mail: Target = recipient or -1 for everyone, A = template, B = player mentioned in the template or -1.</summary>
        SendMail = 10,
        /// <summary>Host only, WeekResults: end the week now.</summary>
        HostContinue = 11,
        /// <summary>v3.1: the fired player, during WeekResults, picks next week's manager (A = index into LineManagerIds.All).</summary>
        PickNextManager = 12,
    }

    public enum LineFailure : byte
    {
        None = 0,
        WrongPhase = 1,
        StaleWeek = 2,
        Duplicate = 3,
        InvalidTarget = 4,
        Self = 5,
        Busy = 6,
        PhoneDead = 7,
        ComputerBlocked = 8,
        AlreadyUsed = 9,
        NotAvailable = 10,
        NoAttemptsLeft = 11,
        NotUnlocked = 12,
        AlreadyInCall = 13,
        NotInCall = 14,
        NotRinging = 15,
        BadParam = 16,
        Absent = 17,
        HostOnly = 18,
        LimitReached = 19,
        RoundSolved = 20,
    }

    public enum LineAudience : byte { All = 0, Player = 1, Host = 2 }

    public enum LineEventType : byte
    {
        None = 0,
        /// <summary>A = (int)LinePhase.</summary>
        PhaseChanged = 1,
        /// <summary>To the callee: Actor = caller.</summary>
        CallIncoming = 2,
        /// <summary>To both parties: Actor = caller, Target = callee, A = call id.</summary>
        CallConnected = 3,
        /// <summary>To both parties: Actor = the other side, A = (int)CallEndReason.</summary>
        CallEnded = 4,
        /// <summary>To the caller: Target = callee who is busy.</summary>
        CallBusy = 5,
        /// <summary>To the recipient: A = mail id.</summary>
        MailReceived = 6,
        /// <summary>Public: Actor submitted (or changed) a round choice.</summary>
        RoundSubmitted = 7,
        /// <summary>Public: Actor tried the Shared Password; A = correct digit count.</summary>
        AttemptMade = 8,
        /// <summary>To the victim: A = (int)TrickType (coffee, dead line, internet).</summary>
        TrickHit = 9,
        /// <summary>Public: the office internet went down (no actor).</summary>
        InternetDown = 10,
        /// <summary>To the actor: A = (int)TrickType, Target = victim.</summary>
        TrickUsed = 11,
        /// <summary>To the tipper: Target.</summary>
        TipFiled = 12,
        /// <summary>Public: Actor has voted (not for whom).</summary>
        VoteCast = 13,
        /// <summary>Public: Target receives today's warning.</summary>
        IhtarGiven = 14,
        /// <summary>Public: Target is fired.</summary>
        Fired = 15,
        /// <summary>Public: Target's blame changed by A (B = (int)BlameReason).</summary>
        BlameChanged = 16,
        /// <summary>Public: Kayırmacı's favourite of the day is Target.</summary>
        FavouriteNamed = 17,
        /// <summary>Public: recording A (pick index) starts now.</summary>
        RecordingStarted = 18,
        /// <summary>Public: Target (re)connected (A = 1) or dropped (A = 0).</summary>
        Presence = 19,
        /// <summary>Public: the Shared Password was cracked by Actor.</summary>
        PasswordCracked = 20,
        /// <summary>To the tapper: wiretap on Target started (A = 1) or ended (A = 0).</summary>
        Wiretap = 21,
        /// <summary>v3.1: to a busy (or dead-line) player: Actor tried to call you.</summary>
        MissedCall = 22,
        /// <summary>v3.1: public: Actor (the fired player) picked next week's manager; A = index into LineManagerIds.All.</summary>
        NextManagerPicked = 23,
    }

    public enum BlameReason : byte
    {
        None = 0,
        Round = 1,
        Ihtar = 2,
        TrickCaught = 3,
        FalseTip = 4,
        TipConfirmed = 5,
    }

    public enum MailKind : byte
    {
        /// <summary>From a colleague (genuine or spoofed — recipients cannot tell).</summary>
        Personal = 0,
        /// <summary>From the manager (round info, morning memo).</summary>
        Manager = 1,
        /// <summary>From IT/HR.</summary>
        System = 2,
    }

    public enum DossierKind : byte
    {
        None = 0,
        /// <summary>Round = type; Player = most affected (or -1); Arg = round index of the day.</summary>
        RoundOutcome = 1,
        /// <summary>Trick = type; Player = culprit if Named else -1; Victim; TemplateId for fake mail.</summary>
        TrickTrace = 2,
        /// <summary>Player = subject of the tip; Arg = 1 confirmed / 0 false. The tipper is never shown.</summary>
        TipResult = 3,
        /// <summary>Player = author of one complaint; Victim = its target.</summary>
        ComplaintLeak = 4,
        /// <summary>Arg = recording pick index.</summary>
        Recording = 5,
        /// <summary>A tape was erased today (Player = culprit if Named).</summary>
        RecordingErased = 6,
        /// <summary>Player = the favourite of the day.</summary>
        Favourite = 7,
    }
}
