using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    internal sealed partial class LineEngine
    {
        private static readonly LineRecordingPick[] NoPicks = new LineRecordingPick[0];
        public IReadOnlyList<LineRecordingPick> RecordingPicks
        {
            get
            {
                if (Phase != LinePhase.MeetingDossier || meeting == null) return NoPicks;
                // Host integration gets detached records as well; callers cannot mutate the state machine.
                var shell = new LineView { Meeting = meeting };
                return LineCodec.Clone(shell).Meeting.Recordings;
            }
        }

        private void StartDossier(double time)
        {
            EndAllCalls(time, CallEndReason.PhaseEnd);
            meeting = new LineMeetingView { IsFinal = Day == days - 1 };
            foreach (int id in ids) { seats[id].Vote = -1; seats[id].Voted = false; }
            if (favourite >= 0) meeting.Dossier.Add(new LineDossierEntry { Kind = DossierKind.Favourite, Player = favourite });
            for (int i = 0; i < reveals.Count; i++)
            {
                int affected = -1, largest = 0;
                foreach (var delta in reveals[i].Deltas) if (Math.Abs(delta.Delta) > largest) { largest = Math.Abs(delta.Delta); affected = delta.Player; }
                meeting.Dossier.Add(new LineDossierEntry { Kind = DossierKind.RoundOutcome, Round = reveals[i].Type, Arg = reveals[i].Index, Player = affected });
            }
            // Resolve all tips before paying traces: several confirmed tips still pay caught blame exactly once.
            foreach (var tip in tips) if (seats[tip.Target].TrickUsed)
                foreach (var trace in traces) if (trace.Actor == tip.Target) trace.Named = true;
            foreach (var trace in traces)
            {
                if (trace.Named) Blame(trace.Actor, profile.TrickCaughtBlame, BlameReason.TrickCaught, time, trick: trace.Trick);
                if (trace.Trick == TrickType.HatDinle && !trace.Named) continue;
                meeting.Dossier.Add(new LineDossierEntry
                {
                    Kind = trace.Trick == TrickType.KayitSil ? DossierKind.RecordingErased : DossierKind.TrickTrace,
                    Trick = trace.Trick, Player = trace.Named ? trace.Actor : -1, Victim = trace.Victim,
                    TemplateId = trace.Template, Named = trace.Named
                });
            }
            foreach (var tip in tips)
            {
                bool confirmed = seats[tip.Target].TrickUsed;
                Blame(confirmed ? tip.Target : tip.Actor, confirmed ? c.ConfirmedTipBlame : c.FalseTipBlame,
                    confirmed ? BlameReason.TipConfirmed : BlameReason.FalseTip, time);
                meeting.Dossier.Add(new LineDossierEntry { Kind = DossierKind.TipResult, Player = tip.Target, Arg = confirmed ? 1 : 0 });
            }
            if (complaints.Count > 0 && rng.Unit() < profile.LeakChance)
            {
                var complaint = complaints[rng.Index(complaints.Count)];
                meeting.Dossier.Add(new LineDossierEntry { Kind = DossierKind.ComplaintLeak, Player = complaint.Author, Victim = complaint.Target });
            }
            PickRecordings(time);
            Enter(LinePhase.MeetingDossier, time, c.Durations.Dossier + c.Durations.DossierPerRecording * meeting.Recordings.Count);
        }

        private Call WeightedCall(List<Call> available)
        {
            double total = 0; for (int i = 0; i < available.Count; i++) total += available[i].End - available[i].Start;
            double roll = rng.Unit() * total;
            int chosen = available.Count - 1;
            for (int i = 0; i < available.Count; i++) { roll -= available[i].End - available[i].Start; if (roll < 0) { chosen = i; break; } }
            var call = available[chosen]; available.RemoveAt(chosen); return call;
        }
        private void PickRecordings(double time)
        {
            if (!setup.RecordingsEnabled || !c.RecordingsEnabled || c.RecordingCandidates < 1) return;
            var available = new List<Call>();
            foreach (var call in calls)
                if (call.Connected && call.End >= 0 && call.End - call.Start >= c.RecordingMinCallSeconds &&
                    !seats[call.Caller].Erased && !seats[call.Callee].Erased) available.Add(call);
            int count = Math.Min(profile.Recordings, available.Count);
            for (int i = 0; i < count; i++)
            {
                var pick = new LineRecordingPick { Index = i, PlayAt = time + 4 + i * c.Durations.DossierPerRecording, Seconds = c.RecordingSnippetSeconds };
                pick.CandidateCallIds.Add(WeightedCall(available).Id); meeting.Recordings.Add(pick);
                meeting.Dossier.Add(new LineDossierEntry { Kind = DossierKind.Recording, Arg = i });
            }
            for (int i = 0; i < count; i++)
                while (available.Count > 0 && meeting.Recordings[i].CandidateCallIds.Count < c.RecordingCandidates)
                    meeting.Recordings[i].CandidateCallIds.Add(WeightedCall(available).Id);
        }

        public void ReportRecording(int pickIndex, int chosenCallId, double time)
        {
            Tick(time);
            if (Phase != LinePhase.MeetingDossier || pickIndex < 0 || pickIndex >= meeting.Recordings.Count) return;
            var pick = meeting.Recordings[pickIndex];
            if (pick.Played || (chosenCallId != -1 && !pick.CandidateCallIds.Contains(chosenCallId))) return;
            pick.Played = true; pick.ChosenCallId = chosenCallId;
            if (chosenCallId >= 0)
            {
                var call = FindCall(chosenCallId); pick.Caller = call.Caller; pick.Callee = call.Callee; pick.CallStart = call.Start;
                Emit(LineEventType.RecordingStarted, time, a: pickIndex);
            }
            Changed();
        }

        private int[] MeetingCounts()
        {
            int[] counts = new int[6]; meeting.Votes.Clear();
            foreach (int id in ids)
            {
                int target = seats[id].Connected && seats[id].Voted ? seats[id].Vote : -1;
                meeting.Votes.Add(new LineVoteView { Voter = id, Target = target }); if (target >= 0) counts[target]++;
            }
            return counts;
        }
        private void DailyVerdict(double time)
        {
            int winner = Plurality(MeetingCounts(), true); meeting.IhtarTo = winner;
            if (winner >= 0)
            {
                seats[winner].Ihtars++; Blame(winner, profile.IhtarBlame, BlameReason.Ihtar, time);
                Emit(LineEventType.IhtarGiven, time, target: winner);
            }
        }
        private void StartDefense(double time)
        {
            meeting.DefenseOrder.AddRange(ids);
            meeting.DefenseOrder.Sort((a, b) =>
            {
                int compare = seats[b].Blame.CompareTo(seats[a].Blame);
                if (compare == 0) compare = seats[b].Ihtars.CompareTo(seats[a].Ihtars);
                return compare == 0 ? a.CompareTo(b) : compare;
            });
            defenderIndex = 0; Enter(LinePhase.FinalDefense, time, c.Durations.DefensePerPlayer * ids.Count);
        }
        public bool MaySpeak(int player)
        { return HasSeat(player) && (Phase != LinePhase.FinalDefense || meeting.DefenseOrder[defenderIndex] == player); }

        private void FinalVerdict(double time)
        {
            int[] counts = MeetingCounts(); var tied = new List<int>();
            int maxScore = int.MinValue, maxIhtars = int.MinValue, maxBlame = int.MinValue;
            foreach (int id in ids)
            {
                var s = seats[id]; int score = s.Blame + profile.FinalVoteWeight * counts[id];
                meeting.Scores.Add(new LineFireScore { Player = id, Blame = s.Blame, Votes = counts[id], Ihtars = s.Ihtars, Score = score });
                if (score > maxScore || (score == maxScore && s.Ihtars > maxIhtars) || (score == maxScore && s.Ihtars == maxIhtars && s.Blame > maxBlame))
                { tied.Clear(); maxScore = score; maxIhtars = s.Ihtars; maxBlame = s.Blame; }
                if (score == maxScore && s.Ihtars == maxIhtars && s.Blame == maxBlame) tied.Add(id);
            }
            meeting.Fired = tied[rng.Index(tied.Count)]; Emit(LineEventType.Fired, time, target: meeting.Fired);
        }

        private int PriorPoints(int id)
        { return setup.History != null && setup.History.SessionPoints.Count > id ? setup.History.SessionPoints[id] : 0; }
        private void FinishResult()
        {
            result = new LineWeekResult { WeekId = setup.WeekId, ManagerProfileId = profile.Id, Days = days, Fired = meeting.Fired };
            int min = int.MaxValue; foreach (int id in ids) min = Math.Min(min, seats[id].Blame);
            foreach (int id in ids)
            {
                var s = seats[id]; bool survived = id != meeting.Fired, hit = s.Agenda == meeting.Fired, employee = s.Blame == min;
                int points = survived ? c.SurvivePoints + (hit ? c.AgendaPoints : 0) + (employee ? c.EmployeeOfWeekPoints : 0) : 0;
                result.Players.Add(new LineWeekPlayerResult { Player = id, Blame = s.Blame, Ihtars = s.Ihtars, Survived = survived,
                    AgendaTarget = s.Agenda, AgendaHit = hit, EmployeeOfWeek = employee, Trick = s.Trick, TricksUsed = s.TricksUsed,
                    Points = points, SessionPoints = PriorPoints(id) + points });
            }
            result.ResultHash = LineCodec.HashResult(result); Changed();
        }

        public LineSessionHistory NextHistory()
        {
            if (!IsFinished) throw new InvalidOperationException("The week is not finished.");
            var history = setup.History == null ? new LineSessionHistory() : LineCodec.Clone(setup.History);
            if (result == null) return history;
            history.WeeksPlayed++; history.PastManagers.Add(profile.Id);
            history.SeenRounds.Clear(); history.SeenRounds.AddRange(seen);
            history.UsedTopics.Clear(); history.UsedTopics.AddRange(usedTopics);
            while (history.SessionPoints.Count < 6) history.SessionPoints.Add(0);
            while (history.TimesFired.Count < 6) history.TimesFired.Add(0);
            foreach (var player in result.Players) history.SessionPoints[player.Player] = player.SessionPoints;
            history.TimesFired[result.Fired]++; return history;
        }
    }
}
