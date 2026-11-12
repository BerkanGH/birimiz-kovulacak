using BirimizKovulacak.Core;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 meeting room sentences ("v5.meeting.*" keys): the one-line "Şimdi: …" hint of the strip (the same decisions as
    /// the old meeting screen's focus card, pointing at the folder, the ballot paper, the whiteboard or the projector), the
    /// use prompts and the name tag line. Names stay in "Label: Name" positions (no Turkish suffixes on names).
    /// </summary>
    public static class MeetingText
    {
        static string T(string key) { return PlayText.T(key); }
        static string F(string key, params object[] a) { return PlayText.F(key, a); }
        static string Key(UnityEngine.InputSystem.InputAction a) { return OfficeText.Key(a); }

        /// <summary>A vote I can (still) cast on paper: a vote phase and I have ballots.</summary>
        public static bool CanVote(LineView v)
        {
            return (v.Phase == LinePhase.MeetingVote || v.Phase == LinePhase.CrisisVote || v.Phase == LinePhase.FinalVote) && v.Mine.Ballots.Count > 0;
        }

        /// <summary>What to do now in the meeting room; <paramref name="todo"/> = something is still expected of me.</summary>
        public static string Now(LineClient lc, LineView v, bool cast, out bool todo)
        {
            todo = false;
            string e = Key(GameInput.Interact), q = Key(GameInput.Computer), tab = Key(GameInput.Phone);
            var mt = v.Meeting;
            var fv = v.Final;
            var meP = lc.Player(lc.MySlot);
            switch (v.Phase)
            {
                case LinePhase.MeetingDossier:
                case LinePhase.MeetingDiscussion:
                    if (mt != null && mt.FailedTasks.Count == 0) return F("meeting.now.clean", q);
                    return F(v.Features.InvestigationOpen || PlayBlocks.LeakableCount(v) > 0 ? "meeting.now.discuss_actions" : "meeting.now.discuss", q, tab);
                case LinePhase.MeetingVote:
                case LinePhase.CrisisVote:
                case LinePhase.FinalVote:
                    if (!CanVote(v)) return T(meP != null && meP.Fired ? "meeting.now.fired" : "meeting.now.no_vote");
                    if (cast) return F(v.Phase == LinePhase.FinalVote ? "meeting.now.cast_final" : "meeting.now.cast", e);
                    todo = true;
                    if (v.Phase == LinePhase.CrisisVote) return F("meeting.now.vote_crisis", lc.Config.CrisisBlame, e);
                    if (v.Phase == LinePhase.FinalVote) return F(fv != null && fv.Revote ? "meeting.now.vote_revote" : "meeting.now.vote_final", e);
                    return F("meeting.now.vote", v.Mine.Ballots.Count, e);
                case LinePhase.MeetingVerdict:
                    return T("meeting.now.verdict");
                case LinePhase.CrisisVerdict:
                    return T(meP != null && meP.Candidate ? "meeting.now.crisis_verdict_me" : "meeting.now.crisis_verdict");
                case LinePhase.FinalDiscussion:
                    if (meP != null && meP.Candidate) { todo = true; return T("meeting.now.final_me"); }
                    return F("meeting.now.final", q);
                case LinePhase.FinalDefense:
                    if (fv != null && fv.Defender == lc.MySlot) { todo = true; return T("meeting.now.defend_me"); }
                    return fv != null && fv.Defender >= 0 ? F("meeting.now.defend", PlayText.Name(fv.Defender), tab) : PlayText.Hint(v.Phase);
                case LinePhase.FinalVerdict:
                    if (fv != null && fv.Firings.Count > 0) return F("meeting.now.fired_desk", PlayText.Name(fv.Firings[fv.Firings.Count - 1].Player), fv.Firings[fv.Firings.Count - 1].Desk);
                    return PlayText.Hint(v.Phase);
                case LinePhase.Results:
                    return F("meeting.now.results", tab, q);
                default:
                    return PlayText.Hint(v.Phase);
            }
        }

        /// <summary>Prompt for a meeting object under the crosshair ("E — …").</summary>
        public static string Use(MeetingTarget t, LineView v)
        {
            switch (t)
            {
                case MeetingTarget.Folder: return T("meeting.use.folder");
                case MeetingTarget.Ballot: return T("meeting.use.ballot");
                case MeetingTarget.Board: return T("meeting.use.board");
                case MeetingTarget.Projector: return T("meeting.use.projector");
                default: return "";
            }
        }

        /// <summary>Second line of a colleague's name tag: suç, tags, vote status.</summary>
        public static string NameInfo(LineView v, LinePlayerView p)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(F("players.blame", p.Blame));
            if (p.Fired) sb.Append(" · ").Append(T("players.fired"));
            else if (p.Candidate) sb.Append(" · ").Append(T("players.candidate"));
            if (p.IsBot) sb.Append(" · ").Append(T("players.bot"));
            if (!p.Connected) sb.Append(" · ").Append(T("players.offline"));
            // v5 step 3b: a paper in the box (a blank one in the final too), never what is on it
            if (v.Features.VoteOpen && !p.Fired) sb.Append(" · ").Append(T(BallotDrops.InBox(v, p) ? "meeting.name.voted" : "meeting.name.voting"));
            return sb.ToString();
        }
    }
}
