using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>Per-player views (spec §11). Allow-list: only what the rules reveal to this player right now.</summary>
    internal sealed partial class LineEngine
    {
        private bool TaskVisible
        {
            get { return Current != null && (Phase == LinePhase.TaskPlay || Phase == LinePhase.TaskReveal || Phase == LinePhase.Break); }
        }

        private bool MeetingVisible
        {
            get { return meeting != null && (Phase >= LinePhase.MeetingDossier && Phase <= LinePhase.FinalVerdict); }
        }

        public LineView BuildView(int player, double time)
        {
            Tick(time);
            if (!HasSeat(player)) throw new ArgumentException("Unknown recipient.");
            var s = seats[player];
            var v = new LineView
            {
                WeekId = setup.WeekId, MatchId = setup.MatchId, StateVersion = StateVersion, Me = player, Now = time,
                Phase = Phase, PhaseStartTime = phaseStart, PhaseEndTime = PhaseEndTime, Day = Day,
                TaskNumber = TaskVisible ? Current.Number : -1,
            };
            v.Features = new LineFeaturesView
            {
                RecordingsEnabled = RecordingsOn, CallsOpen = CallsOpen && !IsFinished, OpenVoice = !CallsOpen,
                TransfersOpen = TransfersOpen, TextsOpen = TextsOpen, MeetingChatOpen = ChatOpen,
                WhistleOpen = WhistleWindow, InvestigationOpen = InvestigationOpen, LeakOpen = LeakOpen && RecordingsOn,
                VoteOpen = (Phase == LinePhase.MeetingVote || Phase == LinePhase.CrisisVote || Phase == LinePhase.FinalVote) && !s.Fired,
                PranksEnabled = PranksOn, PranksOpen = PrankSessionNow != LinePranks.SessionNone,
            };
            foreach (int id in ids)
            {
                var o = seats[id];
                v.Players.Add(new LinePlayerView
                {
                    Id = id, Name = o.Info.Name, ColorIndex = o.Info.ColorIndex, IsBot = o.Info.IsBot, Connected = o.Connected,
                    Blame = o.Blame, Submitted = Phase == LinePhase.TaskPlay && Current.Submitted[id],
                    Voted = o.Ballots.Length > 0 && Array.IndexOf(o.Ballots, -1) < 0,
                    Candidate = o.Candidate, Fired = o.Fired,
                });
            }
            v.LineBoard = LineBoard();
            v.Mine = MineView(player, s);
            if (TaskVisible) v.Task = TaskView(player);
            if (MeetingVisible) v.Meeting = MeetingView();
            if (Phase >= LinePhase.CrisisVote && Phase <= LinePhase.Results) v.Final = FinalView();
            if (Phase == LinePhase.Results && result != null) v.Results = result;
            v.Pranks = PranksView(player, time);
            return LineCodec.Clone(v);
        }

        private LineMeView MineView(int player, Seat s)
        {
            var m = new LineMeView
            {
                Prim = s.Prim, Phone = s.Phone, CallLog = s.Log, Transfers = s.Transfers, RecorderUsesLeft = RecordingsOn ? s.RecorderUses : 0,
                Recorded = RecordedBy(player), InvestigationUsed = s.InvestigationUsed, Ballots = new List<int>(s.Ballots),
                WitnessOf = WitnessOf(player), InvestigationTargets = InvestigationTargets(player), MessageSeq = s.MessageSeq,
                TextsLeft = Math.Max(0, c.TextsPerSegment - s.Texts), MeetingChatLeft = Math.Max(0, c.MeetingChatPerSegment - s.Chats),
                NextMessageAt = s.NextMessage, NextTransferAt = s.NextTransfer,
            };
            if (TaskVisible) m.Group = new List<int>(Current.GroupFor(player));
            return m;
        }

        private LineTaskView TaskView(int player)
        {
            var t = Current;
            var v = new LineTaskView
            {
                Kind = t.Kind, Number = t.Number, Family = LineTasks.Family(t.Kind), PlayEndTime = t.PlayEnd,
                Group = new List<int>(t.GroupFor(player)), Needed = t.Needed, Submitted = t.Submitted[player],
                MyChoice = t.Choice[player], Revealed = t.Resolved,
            };
            if (!t.Resolved) return v;
            foreach (int id in t.GroupFor(player))
                v.Choices.Add(new LineChoiceView { TaskNumber = t.Number, Task = t.Kind, Player = id, Choice = t.Choice[id], Payout = id == player ? t.Payout[id] : -1 });
            v.MyPayout = t.Payout[player];
            if (t.Kind == TaskKind.ClientBonus) v.Takers = t.Takers;
            v.Roster.AddRange(t.Roster);
            return v;
        }

        private LineMeetingView MeetingView()
        {
            var v = new LineMeetingView { Index = meeting.Index, VoteableCount = meeting.Voteable };
            v.FailedTasks.AddRange(meeting.Failed);
            v.Dossier.AddRange(meeting.Dossier);
            v.Roster.AddRange(meeting.Roster);
            foreach (var p in picks) v.Recordings.Add(CopyPick(p));
            v.BlameGained.AddRange(meeting.BlameGained);
            return v;
        }

        private LineFinalView FinalView()
        {
            var v = new LineFinalView { Round = final.Round, Revote = final.Revote, FiringsTotal = final.FiringsTotal };
            if (Phase != LinePhase.CrisisVote) v.Crisis.AddRange(final.Crisis);
            v.Candidates.AddRange(final.Candidates);
            v.DefenseOrder.AddRange(final.DefenseOrder);
            if (Phase == LinePhase.FinalDefense && defenderIndex < final.DefenseOrder.Count)
            {
                v.Defender = final.DefenseOrder[defenderIndex];
                v.DefenderEndTime = DefenderEnd(defenderIndex);
            }
            if (Phase == LinePhase.FinalVote) v.Options.AddRange(final.Options);
            v.Firings.AddRange(final.Firings);
            return v;
        }
    }
}
