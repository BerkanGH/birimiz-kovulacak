using System;
using BirimizKovulacak.Core;
using BirimizKovulacak.Voice;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Default host-side voice providers built from <see cref="HostMatchController"/>'s public state:
    /// <list type="bullet">
    /// <item>active = occupied, connected, human slot (host-side virtual bots have no client to hear/speak);</item>
    /// <item>positions = the slot's <see cref="PlayerAvatar"/> transform;</item>
    /// <item>mode = from the match phase (<see cref="ModeForPhase"/>), lobby (no engine) = AllHear;</item>
    /// <item>walls = Physics.Linecast at mouth height against the Walls and Glass layers (glass muffles voice even
    /// though it does not block sight);</item>
    /// <item>calls = <see cref="CallPartnerResolver"/> (the lead wires it to the engine's call state; until then
    /// nobody is in a call).</item>
    /// </list>
    /// </summary>
    public sealed class HostMatchVoiceBridge : IVoicePositions, IVoiceCallState, IVoiceModeProvider, IVoiceOcclusion, IVoiceClientMap
    {
        readonly HostMatchController _host;
        readonly int _occluderMask;

        /// <summary>slot → connected call partner slot, or -1. Must return -1 while ringing.</summary>
        public Func<int, int> CallPartnerResolver;
        /// <summary>Optional: false silences a speaker (e.g. only the defending player talks during a defense).</summary>
        public Func<int, bool> SpeakerFilter;
        /// <summary>Forces a mode regardless of the phase (null = automatic).</summary>
        public VoiceMode? ModeOverride { get; set; }
        public float MouthHeight = 1.5f;

        public HostMatchController Host => _host;

        public HostMatchVoiceBridge(HostMatchController host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _occluderMask = LayerMask.GetMask(Layers.WallsName, Layers.GlassName);
        }

        /// <summary>Lobby / meeting / results: everyone hears everyone. Briefing..FinalInspection: office rules.</summary>
        public static VoiceMode ModeForPhase(MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.Briefing:
                case MatchPhase.Work1:
                case MatchPhase.Raid:
                case MatchPhase.Work2:
                case MatchPhase.FinalInspection:
                    return VoiceMode.Office;
                default: // None (lobby), Meeting*, Results, Abandoned
                    return VoiceMode.AllHear;
            }
        }

        HostMatchController.Slot SlotAt(int slot)
        {
            var slots = _host.Slots;
            return slot >= 0 && slot < slots.Length ? slots[slot] : null;
        }

        public bool IsActive(int slot)
        {
            var s = SlotAt(slot);
            return s != null && s.Occupied && s.Connected && !s.Virtual;
        }

        public bool TryGetPosition(int slot, out VoiceVec3 position)
        {
            var avatar = PlayerAvatar.BySlot(slot);
            if (avatar == null) { position = default; return false; }
            Vector3 p = avatar.transform.position;
            position = new VoiceVec3(p.x, p.y, p.z);
            return true;
        }

        public int GetCallPartner(int slot)
        {
            return CallPartnerResolver != null ? CallPartnerResolver(slot) : -1;
        }

        public VoiceMode CurrentMode
        {
            get
            {
                if (ModeOverride.HasValue) return ModeOverride.Value;
                var engine = _host.Engine;
                return ModeForPhase(engine != null ? engine.Phase : MatchPhase.None);
            }
        }

        public bool CanSpeak(int slot) { return SpeakerFilter == null || SpeakerFilter(slot); }

        public bool IsOccluded(in VoiceVec3 from, in VoiceVec3 to)
        {
            var a = new Vector3(from.X, from.Y + MouthHeight, from.Z);
            var b = new Vector3(to.X, to.Y + MouthHeight, to.Z);
            return Physics.Linecast(a, b, _occluderMask, QueryTriggerInteraction.Ignore);
        }

        public int SlotOfClient(ulong clientId)
        {
            var slots = _host.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s.Occupied && s.Connected && !s.Virtual && s.ClientId == clientId) return i;
            }
            return -1;
        }

        public bool TryGetClient(int slot, out ulong clientId)
        {
            var s = SlotAt(slot);
            if (s != null && s.Occupied && s.Connected && !s.Virtual && s.ClientId != ulong.MaxValue)
            {
                clientId = s.ClientId;
                return true;
            }
            clientId = ulong.MaxValue;
            return false;
        }
    }
}
