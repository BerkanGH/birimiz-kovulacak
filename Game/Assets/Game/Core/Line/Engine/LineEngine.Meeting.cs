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
                meeting.Dossier.Add(new LineDossierEntry { Kind = DossierKind.TipResult, Player = tip.Target,
                    Arg = confirmed ? 1 : 0, Victim = confirmed ? -1 : tip.Actor });
            }
            if (complaints.Count > 0 && rng.Unit() < profile.LeakChance)
            {
                var complaint = complaints[rng.Index(complaints.Count)];
                meeting.Dossier.Add(new LineDossierEntry { Kind = DossierKind.ComplaintLeak, Player = complaint.Author, Victim = complaint.Target });
            }
            PickRecordings(time);
            if (meeting.IsFinal) BuildShortlist();
            Enter(LinePhase.MeetingDossier, time, c.Durations.Dossier + c.Durations.DossierPerRecording * meeting.Recordings.Count);
        }

        private double RecordingWeight(Call call)
        {
            bool focus = false;
            foreach (var reveal in reveals)
            {
                if (reveal.Hidden == call.Caller || reveal.Hidden == call.Callee) focus = true;
                foreach (var pair in reveal.Pairs)
                {
                    bool caller = call.Caller == pair.A || call.Caller == pair.B || call.Caller == pair.C;
                    bool callee = call.Callee == pair.A || call.Callee == pair.B || call.Callee == pair.C;
                    if (caller && callee) focus = true;
                }
            }
            // A call meeting both criteria receives the multiplier once, not once per reason.
            return (call.End - call.Start) * (focus ? c.RecordingFocusWeight : 1);
        }
        private Call WeightedCall(List<Call> available)
        {
            double total = 0; for (int i = 0; i < available.Count; i++) total += RecordingWeight(available[i]);
            double roll = rng.Unit() * total;
            int chosen = available.Count - 1;
            for (int i = 0; i < available.Count; i++) { roll -= RecordingWeight(available[i]); if (roll < 0) { chosen = i; break; } }
            var call = available[chosen]; available.RemoveAt(chosen); return call;
        }
        private void PickRecordings(double time)
        {
            if (!setup.RecordingsEnabled || !c.RecordingsEnabled || c.RecordingCandidates < 1) return;
            var available = new List<Call>();
            foreach (var call in calls)
                if (call.Connected && call.End >= 0 && call.End - call.Start >= c.RecordingMinCallSeconds &&
                    call.Caller != call.Callee) available.Add(call);
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
            int winner = Plurality(MeetingCounts(), false);
            if (winner >= 0 && seats[winner].Ihtars >= c.MaxIhtarsPerPlayerPerWeek) winner = -1;
            meeting.IhtarTo = winner;
            if (winner >= 0)
            {
                seats[winner].Ihtars++; Blame(winner, profile.IhtarBlame, BlameReason.Ihtar, time);
                Emit(LineEventType.IhtarGiven, time, target: winner);
            }
        }
        private void BuildShortlist()
        {
            var ranking = new List<int>(ids);
            rng.Shuffle(ranking);
            var seedRank = new int[6];
            for (int i = 0; i < ranking.Count; i++) seedRank[ranking[i]] = i;
            ranking.Sort((a, b) =>
            {
                int compare = seats[b].Blame.CompareTo(seats[a].Blame);
                if (compare == 0) compare = seats[b].Ihtars.CompareTo(seats[a].Ihtars);
                return compare == 0 ? seedRank[a].CompareTo(seedRank[b]) : compare;
            });
            int count = Math.Min(ids.Count, ids.Count <= 4 ? c.FinalShortlistSmall : c.FinalShortlistLarge);
            for (int i = 0; i < count; i++) meeting.Shortlist.Add(ranking[i]);
            meeting.DefenseOrder.AddRange(meeting.Shortlist);
        }
        private void StartDefense(double time)
        {
            defenderIndex = 0;
            Enter(LinePhase.FinalDefense, time, c.Durations.DefensePerPlayer * meeting.DefenseOrder.Count);
        }
        public bool MaySpeak(int player)
        { return HasSeat(player) && (Phase != LinePhase.FinalDefense || meeting.DefenseOrder[defenderIndex] == player); }

        private void FinalVerdict(double time)
        {
            int[] counts = MeetingCounts(); var tied = new List<int>();
            int maxVotes = int.MinValue, maxIhtars = int.MinValue, maxBlame = int.MinValue;
            foreach (int id in ids)
            {
                var s = seats[id]; int votes = counts[id];
                meeting.Scores.Add(new LineFireScore { Player = id, Blame = s.Blame, Votes = votes, Ihtars = s.Ihtars, Score = votes });
                if (!meeting.Shortlist.Contains(id)) continue;
                if (votes > maxVotes || (votes == maxVotes && s.Blame > maxBlame) ||
                    (votes == maxVotes && s.Blame == maxBlame && s.Ihtars > maxIhtars))
                { tied.Clear(); maxVotes = votes; maxIhtars = s.Ihtars; maxBlame = s.Blame; }
                if (votes == maxVotes && s.Ihtars == maxIhtars && s.Blame == maxBlame) tied.Add(id);
            }
            // Reuse the seeded shortlist order as the final tie-break. With no votes the final
            // therefore preserves the published blame ranking instead of drawing a new tie-break.
            foreach (int id in meeting.Shortlist) if (tied.Contains(id)) { meeting.Fired = id; break; }
            Emit(LineEventType.Fired, time, target: meeting.Fired);
        }

        private int PriorPoints(int id)
        { return setup.History != null && setup.History.SessionPoints.Count > id ? setup.History.SessionPoints[id] : 0; }
        private void FinishResult()
        {
            result = new LineWeekResult { WeekId = setup.WeekId, ManagerProfileId = profile.Id, Days = days, Fired = meeting.Fired };
            int min = int.MaxValue, minCount = 0;
            foreach (int id in ids)
            {
                if (seats[id].Blame < min) { min = seats[id].Blame; minCount = 1; }
                else if (seats[id].Blame == min) minCount++;
            }
            foreach (int id in ids)
            {
                var s = seats[id]; bool survived = id != meeting.Fired, hit = s.Agenda == meeting.Fired, employee = minCount == 1 && s.Blame == min;
                int agendaIhtars = seats[s.Agenda].Ihtars;
                int points = survived ? c.SurvivePoints + agendaIhtars * c.AgendaPointsPerIhtar +
                    (hit ? c.AgendaPoints : 0) + (employee ? c.EmployeeOfWeekPoints : 0) : 0;
                result.Players.Add(new LineWeekPlayerResult { Player = id, Blame = s.Blame, Ihtars = s.Ihtars, Survived = survived,
                    AgendaTarget = s.Agenda, AgendaHit = hit, AgendaIhtars = agendaIhtars,
                    EmployeeOfWeek = employee, Trick = s.Trick, TricksUsed = s.TricksUsed,
                    Points = points, SessionPoints = PriorPoints(id) + points });
            }
            result.ResultHash = LineCodec.HashResult(result); Changed();
        }

        private LineFailure PickNextManager(int id, LineAction action, double time)
        {
            if (Phase != LinePhase.WeekResults || result == null) return LineFailure.WrongPhase;
            if (id != result.Fired) return LineFailure.NotAvailable;
            if (action.A < 0 || action.A >= LineManagerIds.All.Length) return LineFailure.BadParam;
            string choice = LineManagerIds.All[action.A];
            if (choice == profile.Id) return LineFailure.BadParam;
            result.NextManagerPick = choice;
            result.ResultHash = LineCodec.HashResult(result);
            Emit(LineEventType.NextManagerPicked, time, actor: id, a: action.A);
            return LineFailure.None;
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
            while (history.HiddenRoles.Count < 6) history.HiddenRoles.Add(0);
            while (history.RoundUses.Count < 7) history.RoundUses.Add(0);
            foreach (var player in result.Players) history.SessionPoints[player.Player] = player.SessionPoints;
            foreach (int id in ids) history.HiddenRoles[id] += hiddenRoleUses[id];
            for (int type = 1; type < roundUses.Length; type++) history.RoundUses[type] += roundUses[type];
            history.NextManager = result.NextManagerPick;
            history.TimesFired[result.Fired]++; return history;
        }
    }
}
