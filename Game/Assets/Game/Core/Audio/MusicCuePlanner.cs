using System;

namespace BirimizKovulacak.Core
{
    /// <summary>Music cue ids; each maps to Resources/music/&lt;id&gt;.</summary>
    public static class MusicCues
    {
        public const string MenuLobby = "menu_lobby";
        public const string OfficeWork = "office_work";
        public const string Deadline = "deadline";
        public const string MeetingIntro = "meeting_intro";
        public const string MeetingDiscussion = "meeting_discussion";
        public const string FinalVote = "final_vote";
        public const string ResultsSuccess = "results_success";
        public const string ResultsCollapse = "results_collapse";
    }

    /// <summary>Everything the planner needs; filled by the Unity side from existing state only (no microphone analysis).</summary>
    public struct MusicState
    {
        /// <summary>False = no match view (menu / lobby / disconnected).</summary>
        public bool HasView;
        public LinePhase Phase;
        public int WeekId;
        /// <summary>Seconds left in the current phase (only meaningful in TaskPlay), and the phase length.</summary>
        public double PhaseRemaining;
        public double PhaseLength;
        /// <summary>v5 has no company closure; kept so a future "bad ending" can pick the collapse cue (always false today).</summary>
        public bool CompanyClosed;
        /// <summary>Meeting tape reveal is playing (VoiceSystem.RecordingActive).</summary>
        public bool RecordingActive;
        /// <summary>My phone call is connected.</summary>
        public bool PhoneConnected;
        /// <summary>Anyone in the view is speaking over voice chat.</summary>
        public bool AnySpeaking;
    }

    public struct MusicPlan
    {
        /// <summary>Cue id, or null for silence.</summary>
        public string Cue;
        /// <summary>False for stingers: play once, then stay silent until the cue changes.</summary>
        public bool Loop;
        /// <summary>Gain multiplier 0..1 from ducking inputs (before the user music volume).</summary>
        public float Duck;
        /// <summary>Seconds to crossfade into this cue.</summary>
        public float FadeIn;
    }

    /// <summary>Pure mapping from game state to the music cue and duck level. Stateless: stingers play once because the
    /// director does not restart an unchanged cue; a new WeekId resets the director.</summary>
    public static class MusicCuePlanner
    {
        public const double DeadlineSeconds = 30.0;
        public const double DeadlineFraction = 0.25;
        public const float DuckRecording = 0.08f;
        public const float DuckPhone = 0.35f;
        public const float DuckMeetingSpeech = 0.45f;
        public const float DuckFinalDefense = 0.25f;

        /// <summary>Deadline starts at min(30 s, 25% of the play length) remaining, so short tasks are not all "deadline".</summary>
        public static bool IsDeadline(double remaining, double length)
        {
            if (length <= 0.0 || remaining < 0.0) return false;
            double threshold = Math.Min(DeadlineSeconds, DeadlineFraction * length);
            return remaining <= threshold;
        }

        public static MusicPlan Plan(MusicState s)
        {
            var p = new MusicPlan { Cue = null, Loop = true, Duck = 1f, FadeIn = 1.5f };
            if (!s.HasView || s.Phase == LinePhase.None) { p.Cue = MusicCues.MenuLobby; return Finish(p, s); }
            switch (s.Phase)
            {
                case LinePhase.Abandoned:
                    p.FadeIn = 0.6f; return p; // stop
                case LinePhase.Intro:
                case LinePhase.TaskReveal:
                case LinePhase.Break:
                case LinePhase.CrisisCalls:
                    p.Cue = MusicCues.OfficeWork; break;
                case LinePhase.TaskPlay:
                    p.Cue = IsDeadline(s.PhaseRemaining, s.PhaseLength) ? MusicCues.Deadline : MusicCues.OfficeWork;
                    p.FadeIn = p.Cue == MusicCues.Deadline ? 2.5f : 1.5f; break;
                case LinePhase.MeetingDossier:
                    p.Cue = MusicCues.MeetingIntro; p.Loop = false; p.FadeIn = 0.15f; break;
                case LinePhase.MeetingDiscussion:
                case LinePhase.MeetingVote:
                case LinePhase.MeetingVerdict:
                case LinePhase.CrisisVote:
                case LinePhase.CrisisVerdict:
                case LinePhase.FinalDiscussion:
                    p.Cue = MusicCues.MeetingDiscussion; p.FadeIn = 2.5f; break;
                case LinePhase.FinalDefense:
                case LinePhase.FinalVote:
                case LinePhase.FinalVerdict:
                    p.Cue = MusicCues.FinalVote; p.FadeIn = 2.5f; break;
                case LinePhase.Results:
                    p.Cue = s.CompanyClosed ? MusicCues.ResultsCollapse : MusicCues.ResultsSuccess; p.Loop = false; p.FadeIn = 0.4f; break;
                default:
                    return p;
            }
            return Finish(p, s);
        }

        static MusicPlan Finish(MusicPlan p, MusicState s)
        {
            float d = 1f;
            bool speechDucks = s.Phase >= LinePhase.MeetingDiscussion && s.Phase <= LinePhase.MeetingVerdict
                || s.Phase >= LinePhase.CrisisVote && s.Phase <= LinePhase.FinalVerdict;
            if (s.HasView && s.Phase == LinePhase.FinalDefense) d = Math.Min(d, DuckFinalDefense);
            if (s.HasView && speechDucks && s.AnySpeaking) d = Math.Min(d, DuckMeetingSpeech);
            if (s.HasView && s.PhoneConnected) d = Math.Min(d, DuckPhone);
            if (s.HasView && s.RecordingActive) d = Math.Min(d, DuckRecording);
            p.Duck = d;
            return p;
        }
    }
}
