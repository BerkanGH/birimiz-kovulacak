using System.Collections.Generic;
namespace BirimizKovulacak.Core
{
    internal sealed class PlayerState
    {
        internal PlayerPublicView Public = new PlayerPublicView();
        internal PrivateView Private = new PrivateView();
        internal AvatarSample Avatar;
        internal InteractionState Interaction;
        internal readonly Queue<uint> Requests = new Queue<uint>();
        internal readonly int[] TaskCounts = new int[4];
        internal int Work2Tasks;
        internal ObjectiveDef Objective;
        internal int ObjectiveTarget = PlayerIds.None;
        internal float FalseAccusationPenalty;
    }
    internal sealed class ItemState
    {
        internal ItemView View = new ItemView();
        internal readonly bool[] KnownBy = new bool[4];
        internal readonly List<int> Handlers = new List<int>();
        internal int LastPlacer = PlayerIds.None;
        internal double PlacedAt = double.NegativeInfinity, MovedAt = double.NegativeInfinity;
        internal string PlacementSlot = "";
        internal bool TamperedThisCarry;
    }
    internal sealed class DeskState
    {
        internal DeskDef Def;
        internal DeskView View = new DeskView();
        internal double NameplateChangedAt = double.NegativeInfinity, ImmuneUntil;
        internal int LockIncident, SpillIncident;
    }
    internal sealed class InteractionState
    {
        internal ActionRequest Request;
        internal double Start, End;
        internal Vec3 StartPosition;
        internal string HeldItem = "", HeldBefore = "", Reservation = "";
        internal int SlotIndex = -1, PhoneCall;
        internal PublicActivity Activity;
        internal SabotageType? Sabotage;
        internal int Victim = PlayerIds.None, DeskIndex = -1;
    }
    internal sealed class IncidentState
    {
        internal IncidentReveal Reveal = new IncidentReveal();
        internal bool Active = true, ConsequenceApplied;
        internal string LocationId = "";
        internal readonly List<int> BlamedPlayers = new List<int>();
    }
    internal sealed class EvidenceState
    {
        internal EvidenceView View = new EvidenceView();
        internal int Sealer = PlayerIds.None, Placer = PlayerIds.None;
        internal double PlacedAt = double.NegativeInfinity;
    }
}
