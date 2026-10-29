namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        partial void CheckReconnectExpiry()
        {
            if (Phase >= MatchPhase.MeetingDiscussion) return;
            foreach (int p in playerIds)
                if (!players[p].Public.Connected && clock >= players[p].Public.DisconnectDeadline)
                { Abandon(AbandonReason.PlayerTimedOut, clock); return; }
        }
    }
}
