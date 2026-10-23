namespace BirimizKovulacak.Core
{
    public sealed partial class MatchEngine
    {
        partial void CheckReconnectExpiry()
        {
            if (IsFinished) return;
            for (int p = 0; p < 4; p++)
                if (!players[p].Public.Connected && clock >= players[p].Public.DisconnectDeadline)
                { Abandon(AbandonReason.PlayerTimedOut, clock); return; }
        }
    }
}
