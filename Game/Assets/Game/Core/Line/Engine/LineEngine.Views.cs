using System;

namespace BirimizKovulacak.Core
{
    internal sealed partial class LineEngine
    {
        public LineView BuildView(int player, double time)
        {
            Tick(time);
            if (!HasSeat(player)) throw new ArgumentException("Unknown view recipient.", nameof(player));
            var s = seats[player];
            var view = new LineView
            {
                WeekId = setup.WeekId, StateVersion = StateVersion, Me = player, Now = time, Phase = Phase,
                PhaseStartTime = phaseStart, PhaseEndTime = PhaseEndTime, Day = Day, DayCount = days, Length = setup.Length,
                FirstWeek = firstWeek, RoundIndex = InRound ? roundIndex : -1, ManagerProfileId = profile.Id, Favourite = favourite,
                Features = new LineFeaturesView { TricksUnlocked = Unlocked, TipLineUnlocked = Unlocked,
                    RecordingsEnabled = setup.RecordingsEnabled && c.RecordingsEnabled, IsLastDay = Day == days - 1,
                    FirstTimeRound = InRound && firstTimeRound, OnboardingDay = firstWeek && Day == 0 },
                Mine = new LineMeView { AgendaTarget = s.Agenda, Trick = s.Trick, TrickUsedToday = s.TrickUsed,
                    TipUsedToday = s.TipUsed, MailsSentToday = s.MailsSent, ComputerBlockedUntil = s.BlockedUntil,
                    ComputerBlockedBy = s.BlockedBy, PhoneDeadUntil = s.DeadUntil, WiretapTarget = s.TapTarget,
                    WiretapUntil = s.TapUntil, Phone = s.Phone, Inbox = s.Inbox, CallLog = s.Log },
                Result = Phase == LinePhase.WeekResults ? result : null
            };
            for (int i = 0; i < c.RoundsPerDay; i++) view.TodayRounds.Add(schedule[Day * c.RoundsPerDay + i]);
            foreach (int id in ids)
            {
                var p = seats[id];
                view.Players.Add(new LinePlayerView { Id = id, Name = p.Info.Name, ColorIndex = p.Info.ColorIndex,
                    Connected = p.Connected, IsBot = p.Info.IsBot, Blame = p.Blame, Ihtars = p.Ihtars,
                    Submitted = (Phase == LinePhase.RoundPlay || Phase == LinePhase.RoundReveal) && p.Submitted,
                    Voted = (Phase == LinePhase.MeetingVote || Phase == LinePhase.FinalVote || Phase == LinePhase.MeetingVerdict || Phase == LinePhase.FinalVerdict) && p.Voted,
                    SessionPoints = PriorPoints(id) });
            }
            view.TodayReveals.AddRange(reveals);
            if (InRound)
            {
                var r = new LineRoundView { Type = round.Type, Index = roundIndex,
                    PlayEndTime = Phase == LinePhase.RoundIntro ? PhaseEndTime + c.Durations.RoundPlay : Phase == LinePhase.RoundPlay ? PhaseEndTime : phaseStart,
                    Submitted = s.Submitted, MyTarget = s.Target, MyA = s.A,
                    AttemptsLeft = round.Type == RoundType.OrtakSifre ? Math.Max(0, c.Rounds.AttemptsPerPlayer - s.Attempts) : 0,
                    Solved = round.Solved, Reveal = Phase == LinePhase.RoundReveal ? round.Reveal : null };
                if (round.Type == RoundType.OrtakSifre)
                { r.CodeLength = ids.Count; r.PositionOwners.AddRange(round.Order); r.Attempts.AddRange(round.Attempts); }
                if (s.BlockedUntil <= time || Phase == LinePhase.RoundReveal)
                {
                    switch (round.Type)
                    {
                        case RoundType.PrimHavuzu: r.Partner = round.Partners[player]; break;
                        case RoundType.FarkliEposta:
                            r.TopicId = round.Topic; r.IsOdd = round.Hidden == player; r.TopicVariant = r.IsOdd ? 1 : 0;
                            if (r.IsOdd) r.GuessOptions.AddRange(round.Options); break;
                        case RoundType.KayipRapor: r.IsCulprit = round.Hidden == player; r.ClueInnocent = round.Clues[player]; break;
                        case RoundType.OrtakSifre:
                            r.DigitPosition = round.Order.IndexOf(player); r.Digit = round.Digits[player]; r.IsSaboteur = round.Hidden == player; break;
                    }
                }
                else { r.MyTarget = -1; r.MyA = -1; }
                view.Round = r;
            }
            if (Phase >= LinePhase.MeetingDossier && Phase <= LinePhase.FinalVerdict)
            {
                // Build an allow-list of public meeting facts. Candidate call ids are host-only.
                var m = new LineMeetingView { IsFinal = meeting.IsFinal, MyVote = s.Vote, IhtarTo = meeting.IhtarTo,
                    Dossier = meeting.Dossier, DefenseOrder = meeting.DefenseOrder,
                    Fired = Phase == LinePhase.FinalVerdict ? meeting.Fired : -1 };
                if (Phase == LinePhase.MeetingVerdict || Phase == LinePhase.FinalVerdict) m.Votes.AddRange(meeting.Votes);
                if (Phase == LinePhase.FinalVerdict) m.Scores.AddRange(meeting.Scores);
                if (Phase == LinePhase.FinalDefense)
                {
                    m.Defender = meeting.DefenseOrder[defenderIndex];
                    m.DefenderEndTime = phaseStart + (defenderIndex + 1) * c.Durations.DefensePerPlayer;
                }
                foreach (var pick in meeting.Recordings)
                    m.Recordings.Add(new LineRecordingPick { Index = pick.Index, PlayAt = pick.PlayAt, Seconds = pick.Seconds,
                        Played = pick.Played, ChosenCallId = pick.ChosenCallId, Caller = pick.Caller, Callee = pick.Callee, CallStart = pick.CallStart });
                view.Meeting = m;
            }
            // Nothing returned by this method aliases engine state, even nested lists and completed reveals.
            return LineCodec.Clone(view);
        }
    }
}
