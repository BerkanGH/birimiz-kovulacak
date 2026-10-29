using System;

namespace BirimizKovulacak.Voice
{
    /// <summary>Host-side: where each player's avatar is and whether the slot takes part in voice.</summary>
    public interface IVoicePositions
    {
        /// <summary>True for an occupied, connected slot with a real (human) client that can speak/listen.</summary>
        bool IsActive(int slot);
        /// <summary>Only used by <see cref="VoiceMode.Office"/> (v2 proximity). A v3 provider may return false.</summary>
        bool TryGetPosition(int slot, out VoiceVec3 position);
    }

    /// <summary>
    /// Host-side phone state. Every answer describes the CONNECTED call only (never a ringing one).
    /// </summary>
    public interface IVoiceCallState
    {
        /// <summary>The connected call partner of <paramref name="slot"/>, or -1 (idle, ringing, declined...).
        /// The router additionally requires both sides to point at each other.</summary>
        int GetCallPartner(int slot);

        /// <summary>v3: id of the connected call of <paramref name="slot"/> (the same value on both sides, unique
        /// until the recordings are cleared, e.g. the engine's CallRecord index), or -1 when not connected. Groups
        /// the call recordings; -1 everywhere simply disables recording (v2).</summary>
        int GetCallId(int slot);

        /// <summary>v3 wiretap: writes the slots currently tapping <paramref name="target"/> into
        /// <paramref name="buffer"/> (length <see cref="VoiceFormat.MaxPlayers"/>) and returns how many. Called for
        /// both sides of every routed call frame, so keep it allocation-free. Return 0 when nobody taps.</summary>
        int GetTappers(int target, int[] buffer);
    }

    /// <summary>Host-side: the current voice mode (from the match phase) and per-speaker permission.</summary>
    public interface IVoiceModeProvider
    {
        VoiceMode CurrentMode { get; }
        /// <summary>False silences a speaker (e.g. only the defending player may talk during a defense).</summary>
        bool CanSpeak(int slot);
    }

    /// <summary>Host-side: whether a wall is between two points (voice gets muffled).</summary>
    public interface IVoiceOcclusion
    {
        bool IsOccluded(in VoiceVec3 from, in VoiceVec3 to);
    }

    /// <summary>Host-side: NGO client id ↔ player slot.</summary>
    public interface IVoiceClientMap
    {
        int SlotOfClient(ulong clientId);
        bool TryGetClient(int slot, out ulong clientId);
    }

    public struct VoiceRoute
    {
        public int Recipient;
        public VoiceFrameFlags Flags;
    }

    public interface IVoiceRouter
    {
        /// <summary>Fills <paramref name="routes"/> with who receives one frame of <paramref name="speaker"/>.</summary>
        int Route(int speaker, VoiceRoute[] routes);
    }

    /// <summary>Distances and levels of the office voice (shared by the host router and client playback).</summary>
    public sealed class VoiceRoutingSettings
    {
        /// <summary>Full volume up to this distance (m).</summary>
        public float FullVolumeDistance = 3f;
        /// <summary>Silent from this distance (m); the host does not send beyond it.</summary>
        public float MaxDistance = 14f;
        /// <summary>Through a wall the voice carries at most this far (m). Default = MaxDistance (brief rule).</summary>
        public float MuffledMaxDistance = 14f;
        /// <summary>Extra gain on muffled voice (on top of distance attenuation).</summary>
        public float MuffledGain = 0.45f;
        /// <summary>Low-pass cutoff for muffled voice (Hz).</summary>
        public float MuffledCutoffHz = 900f;
        /// <summary>Call frames are only routed when both players point at each other.</summary>
        public bool RequireMutualCall = true;
        /// <summary>Height above the avatar root used for the wall test (m).</summary>
        public float MouthHeight = 1.5f;
        /// <summary>Client gain of wiretapped call audio (relative to a normal call).</summary>
        public float TapGain = 0.7f;

        public static readonly VoiceRoutingSettings Default = new VoiceRoutingSettings();
    }

    /// <summary>
    /// Default host routing rules (pure, unit-tested):
    /// <list type="bullet">
    /// <item>Muted: nothing. Inactive speaker or CanSpeak=false: nothing (the v3 final-defense floor).</item>
    /// <item>AllHear: every other active slot, flag AllHear (2D, full volume). No calls, no taps.</item>
    /// <item>Calls (v3 day): only the connected call partner (flag Call; both sides must point at each other) plus
    /// every active slot tapping either side of that call (flags Call|Tap). No positions are needed.</item>
    /// <item>Office (v2): the Calls rules plus proximity voice: active slots within MaxDistance (flag Muffled when
    /// occluded, cut at MuffledMaxDistance). A call stream never goes to anyone else — bystanders only hear the
    /// speaker's physical voice through proximity (eavesdropping).</item>
    /// </list>
    /// Wiretaps are one-way: a tapper only ever RECEIVES; its own voice follows its own routes (its own call, if
    /// any) and never reaches the tapped call. A recipient is never routed twice (a tapping partner keeps its
    /// plain Call route). After each <see cref="Route"/>, <see cref="LastCallId"/> / <see cref="LastCallPartner"/>
    /// describe the call the frame was routed into (-1 = not a call frame) so the host can record it.
    /// Allocation-free.
    /// </summary>
    public sealed class ProximityVoiceRouter : IVoiceRouter
    {
        public IVoicePositions Positions;
        public IVoiceCallState Calls;
        public IVoiceModeProvider Mode;
        public IVoiceOcclusion Occlusion;
        public VoiceRoutingSettings Settings;
        public int SlotCount = VoiceFormat.MaxPlayers;

        readonly int[] _tappers = new int[VoiceFormat.MaxPlayers];

        // Counters (cumulative).
        public long Decisions, Delivered, CallRoutes, TapRoutes, MuffledRoutes, MutedDrops, OutOfRange;

        /// <summary>Connected call id of the last routed frame (-1: not a call frame, or no ids from the provider).</summary>
        public int LastCallId { get; private set; } = -1;
        /// <summary>Call partner of the last routed frame's speaker (-1: not a call frame).</summary>
        public int LastCallPartner { get; private set; } = -1;

        public ProximityVoiceRouter(IVoicePositions positions, IVoiceCallState calls, IVoiceModeProvider mode,
            IVoiceOcclusion occlusion, VoiceRoutingSettings settings = null)
        {
            Positions = positions;
            Calls = calls;
            Mode = mode;
            Occlusion = occlusion;
            Settings = settings ?? VoiceRoutingSettings.Default;
        }

        public int Route(int speaker, VoiceRoute[] routes)
        {
            Decisions++;
            LastCallId = -1;
            LastCallPartner = -1;
            if (routes == null || Positions == null || speaker < 0 || speaker >= SlotCount) return 0;
            VoiceMode mode = Mode != null ? Mode.CurrentMode : VoiceMode.AllHear;
            if (mode == VoiceMode.Muted || !Positions.IsActive(speaker) || (Mode != null && !Mode.CanSpeak(speaker)))
            {
                MutedDrops++;
                return 0;
            }

            int n = 0;
            if (mode == VoiceMode.AllHear)
            {
                for (int r = 0; r < SlotCount && n < routes.Length; r++)
                {
                    if (r == speaker || !Positions.IsActive(r)) continue;
                    routes[n].Recipient = r;
                    routes[n].Flags = VoiceFrameFlags.AllHear;
                    n++;
                }
                Delivered += n;
                return n;
            }

            // Office / Calls: the private call first.
            int partner = ConnectedPartner(speaker);
            if (partner >= 0 && n < routes.Length)
            {
                routes[n].Recipient = partner;
                routes[n].Flags = VoiceFrameFlags.Call;
                n++;
                CallRoutes++;
                LastCallPartner = partner;
                int id = Calls.GetCallId(speaker);
                // Both sides must agree on the id; otherwise the frame is still routed but not attributed to a call.
                LastCallId = id >= 0 && Calls.GetCallId(partner) == id ? id : -1;
                // Taps on either side of the connected call hear both directions.
                n = AddTappers(speaker, speaker, partner, routes, n);
                n = AddTappers(partner, speaker, partner, routes, n);
            }

            if (mode == VoiceMode.Office)
            {
                VoiceVec3 sp;
                if (Positions.TryGetPosition(speaker, out sp))
                {
                    float max2 = Settings.MaxDistance * Settings.MaxDistance;
                    float muffledMax2 = Settings.MuffledMaxDistance * Settings.MuffledMaxDistance;
                    for (int r = 0; r < SlotCount && n < routes.Length; r++)
                    {
                        if (r == speaker || !Positions.IsActive(r) || Contains(routes, n, r)) continue;
                        VoiceVec3 rp;
                        if (!Positions.TryGetPosition(r, out rp)) continue;
                        float d2 = VoiceVec3.DistanceSquared(sp, rp);
                        if (d2 > max2) { OutOfRange++; continue; }
                        bool occluded = Occlusion != null && Occlusion.IsOccluded(sp, rp);
                        if (occluded && d2 > muffledMax2) { OutOfRange++; continue; }
                        routes[n].Recipient = r;
                        routes[n].Flags = occluded ? VoiceFrameFlags.Muffled : VoiceFrameFlags.None;
                        if (occluded) MuffledRoutes++;
                        n++;
                    }
                }
            }
            Delivered += n;
            return n;
        }

        /// <summary>The speaker's partner when the call is connected (mutual, both active), otherwise -1.</summary>
        int ConnectedPartner(int speaker)
        {
            if (Calls == null) return -1;
            int partner = Calls.GetCallPartner(speaker);
            if (partner < 0 || partner >= SlotCount || partner == speaker || !Positions.IsActive(partner)) return -1;
            if (Settings.RequireMutualCall && Calls.GetCallPartner(partner) != speaker) return -1;
            return partner;
        }

        int AddTappers(int target, int speaker, int partner, VoiceRoute[] routes, int n)
        {
            int count = Calls.GetTappers(target, _tappers);
            if (count > _tappers.Length) count = _tappers.Length;
            for (int i = 0; i < count && n < routes.Length; i++)
            {
                int t = _tappers[i];
                if (t < 0 || t >= SlotCount || t == speaker || t == partner || !Positions.IsActive(t) || Contains(routes, n, t)) continue;
                routes[n].Recipient = t;
                routes[n].Flags = VoiceFrameFlags.Call | VoiceFrameFlags.Tap;
                n++;
                TapRoutes++;
            }
            return n;
        }

        static bool Contains(VoiceRoute[] routes, int n, int recipient)
        {
            for (int i = 0; i < n; i++)
                if (routes[i].Recipient == recipient) return true;
            return false;
        }
    }
}
