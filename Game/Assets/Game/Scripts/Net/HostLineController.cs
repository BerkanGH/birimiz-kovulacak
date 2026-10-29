using BirimizKovulacak.Core;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v3 host session: lobby seats (humans + bots), week setup, the rules engine tick, per-recipient views/events,
    /// voice integration. Owner: lead. The lobby UI calls only the public methods below.
    /// </summary>
    public sealed class HostLineController
    {
        /// <summary>Host of the current session (null on clients and offline).</summary>
        public static HostLineController Current { get; internal set; }

        public WeekLength Length { get; private set; } = WeekLength.Short;
        public bool RecordingsEnabled { get; private set; } = true;

        public void AddBot() { }
        public void RemoveBot(int slot) { }
        public void SetLength(WeekLength length) { Length = length; }
        public void SetRecordings(bool on) { RecordingsEnabled = on; }

        /// <summary>False with a localisation key explaining why (e.g. "lobby.need_players").</summary>
        public bool CanStart(out string reasonKey) { reasonKey = "lobby.waiting"; return false; }
        public void StartWeek() { }
        /// <summary>WeekResults: continue to the lobby / next week now.</summary>
        public void ContinueAfterResults() { }
        public void ReturnToLobby() { }
    }
}
