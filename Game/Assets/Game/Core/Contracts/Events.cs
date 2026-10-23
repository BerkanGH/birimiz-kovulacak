using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// Transient notification for feedback (toasts, sounds, bubbles). Durable facts live in <see cref="PlayerView"/>.
    /// The adapter routes by <see cref="Audience"/>: All -> every client, Player -> AudiencePlayer only,
    /// Host -> never sent. Public events must not carry hidden truth (e.g. the actor of a lock).
    /// </summary>
    [Serializable]
    public sealed class GameEvent
    {
        public EventType Type;
        public double Time;
        public Audience Audience;
        public int AudiencePlayer = PlayerIds.None;
        public int Actor = PlayerIds.None;
        public string TargetId = "";
        public string ItemId = "";
        public int IntArg = -1;
        public string StrArg = "";
        public MatchPhase Phase;

        public bool IsVisibleTo(int player)
        {
            if (Audience == Audience.All) return true;
            if (Audience == Audience.Player) return AudiencePlayer == player;
            return false;
        }

        public override string ToString() { return Type + "@" + Time.ToString("0.0") + " a=" + Actor + " t=" + TargetId + " i=" + IntArg + " [" + Audience + (Audience == Audience.Player ? ":" + AudiencePlayer : "") + "]"; }
    }

    /// <summary>Pre-match lobby state, maintained by the network adapter and broadcast to everyone.</summary>
    [Serializable]
    public sealed class LobbyView
    {
        public int ProtocolVersion = Protocol.Version;
        public string BuildId = "";
        public List<LobbySlot> Slots = new List<LobbySlot>();
        public bool MatchRunning;
        public int MatchId;
        /// <summary>Message key shown when the lobby cannot start, e.g. "lobby.need_four".</summary>
        public string StatusKey = "";
    }

    [Serializable]
    public sealed class LobbySlot
    {
        public int Slot;
        public bool Occupied;
        public bool Connected;
        public bool Ready;
        public bool IsHost;
        public string Name = "";
    }

    /// <summary>Payload sent with the transport connection request (connection approval).</summary>
    [Serializable]
    public sealed class ConnectionPayload
    {
        public int ProtocolVersion = Protocol.Version;
        public string BuildId = "";
        public string PlayerName = "";
        /// <summary>Empty for a fresh join; the token received on the first join for a reconnect.</summary>
        public string ReconnectToken = "";
    }

    /// <summary>Host's reply after approval (sent once to the joining client).</summary>
    [Serializable]
    public sealed class WelcomeMessage
    {
        public int Slot;
        public string ReconnectToken = "";
        public bool Reconnected;
        public int MatchId;
    }
}
