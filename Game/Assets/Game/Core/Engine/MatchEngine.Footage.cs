using System;

namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        // Private, compact host samples. Only requested sightings ever become contract DTOs.
        private struct PositionSample
        {
            internal double Time;
            internal Vec3 Position;
            internal string Carrying;
            internal int Player;
            internal PublicActivity Activity;
            internal bool Crouched;
            internal bool Dark;
        }

        private const int MaxPositionSamples = 32768;
        private PositionSample[] positionLog;
        private int positionLogStart, positionLogCount;
        private long positionSampleOrdinal;
        private double footageMatchStart, footageFreezeAt, positionSampleInterval;
        private double nextPositionSample = double.PositiveInfinity;
        private readonly int[] speakerFootageUsed = new int[Protocol.MaxPlayers];
        private readonly int[] informantFootageUsed = new int[Protocol.MaxPlayers];

        private void InitializeFootage()
        {
            footageMatchStart = clock;
            footageFreezeAt = double.PositiveInfinity;
            positionSampleInterval = Finite(Config.Footage.SampleInterval) && Config.Footage.SampleInterval > 0 ? Config.Footage.SampleInterval : 1;
            PhaseDurations d = Config.Durations;
            double duration = Math.Max(0, d.Briefing) + (double)Math.Max(0, d.Work1) + Math.Max(0, d.Raid) + Math.Max(0, d.Work2) + Math.Max(0, d.FinalInspection);
            int capacity = (int)Math.Min(MaxPositionSamples, Math.Max(playerIds.Count, (Math.Ceiling(duration / positionSampleInterval) + 1) * playerIds.Count));
            positionLog = new PositionSample[capacity];
            RecordPositions();
            positionSampleOrdinal = 1;
            nextPositionSample = footageMatchStart + positionSampleInterval;
        }

        private void NextFootageTime(ref double next)
        {
            if (Phase >= MatchPhase.Briefing && Phase <= MatchPhase.FinalInspection)
                next = Math.Min(next, Math.Max(clock, nextPositionSample));
        }

        private void AdvanceFootage()
        {
            if (Phase < MatchPhase.Briefing || Phase > MatchPhase.FinalInspection || clock < nextPositionSample) return;
            RecordPositions();
            positionSampleOrdinal++;
            nextPositionSample = footageMatchStart + positionSampleOrdinal * positionSampleInterval;
        }

        private void FootagePhaseChanged()
        {
            if (Phase == MatchPhase.MeetingDiscussion) footageFreezeAt = phaseStart;
            if (Phase >= MatchPhase.MeetingDiscussion || Phase == MatchPhase.Abandoned) nextPositionSample = double.PositiveInfinity;
        }

        private void RecordPositions()
        {
            foreach (int p in playerIds)
            {
                PlayerState player = players[p];
                if (!player.Public.Connected) continue;
                string carrying = "";
                if (player.Public.CarryingItemId.Length != 0)
                    foreach (ItemState item in items)
                        if (item.View.Id == player.Public.CarryingItemId) { carrying = item.View.Variant; break; }
                int index = (positionLogStart + positionLogCount) % positionLog.Length;
                if (positionLogCount == positionLog.Length) positionLogStart = (positionLogStart + 1) % positionLog.Length;
                else positionLogCount++;
                positionLog[index] = new PositionSample
                {
                    Time = clock, Position = player.Avatar.Position, Player = p, Activity = player.Public.Activity,
                    Carrying = carrying, Crouched = player.Avatar.Crouched, Dark = officeStatus.PowerDown
                };
            }
        }

        private bool FootageMeeting { get { return Phase >= MatchPhase.MeetingDiscussion && Phase <= MatchPhase.MeetingDefense2; } }
        private bool FootageSpeaker(int p)
        {
            return (Phase == MatchPhase.MeetingDefense1 || Phase == MatchPhase.MeetingDefense2) && meeting.Speaker == p;
        }

        private int SpeakerFootageAllowance()
        {
            return Math.Max(0, Config.Footage.SpeakerRequests + (managerProfile == null ? 0 : managerProfile.ExtraFootageRequests));
        }

        private int FootageLeft(int p)
        {
            if (!features.Footage || !FootageMeeting) return 0;
            int left = FootageSpeaker(p) ? Math.Max(0, SpeakerFootageAllowance() - speakerFootageUsed[p]) : 0;
            if (players[p].Private.Role == Role.Informant) left += Math.Max(0, Config.Informant.FootageCharges - informantFootageUsed[p]);
            return left;
        }

        private void SubmitFootage(int p, ActionRequest request, ref bool handled, ref FailureCode failure)
        {
            if (handled || request.Verb != ActionVerb.RequestFootage) return;
            handled = true;
            if (!features.Footage) { failure = FailureCode.FeatureDisabled; return; }
            bool speaker = FootageSpeaker(p), informant = players[p].Private.Role == Role.Informant;
            if (!speaker && !informant) { failure = FailureCode.NotSpeaker; return; }
            if (FootageLeft(p) <= 0) { failure = FailureCode.NoCharges; return; }
            CameraDef camera = Layout.GetCamera(request.TargetId);
            double requestedAt = footageMatchStart + request.IntParam;
            if (camera == null || request.IntParam < 0 || requestedAt > footageFreezeAt)
            { failure = FailureCode.InvalidTarget; return; }

            EvidenceState record = AddEvidence(EvidenceKind.CctvFootage, null, camera.Id, -1, DiscoverySource.Player, p, PlayerIds.None);
            EvidenceView footage = record.View;
            footage.CameraId = camera.Id;
            footage.WindowStart = requestedAt - Math.Max(0, Config.Footage.WindowSeconds);
            footage.WindowEnd = requestedAt + Math.Max(0, Config.Footage.WindowSeconds);
            bool anySample = false, anyLight = false;
            for (int n = 0; n < positionLogCount; n++)
            {
                PositionSample sample = positionLog[(positionLogStart + n) % positionLog.Length];
                if (sample.Time < footage.WindowStart || sample.Time > footage.WindowEnd) continue;
                anySample = true;
                if (sample.Dark) continue;
                anyLight = true;
                if (!camera.Covers(sample.Position)) continue;
                footage.Sightings.Add(new FootageSighting
                {
                    Player = sample.Player, Time = sample.Time, Activity = sample.Activity,
                    Carrying = sample.Carrying, Crouched = sample.Crouched
                });
            }
            footage.NoRecording = anySample && !anyLight;
            if (speaker && speakerFootageUsed[p] < SpeakerFootageAllowance()) speakerFootageUsed[p]++;
            else informantFootageUsed[p]++;
            meeting.PresentedEvidence.Add(footage.Id);
            Emit(EventType.FootageShown, arg: footage.Id);
            failure = FailureCode.None;
        }
    }
}
