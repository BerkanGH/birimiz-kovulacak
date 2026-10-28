using System;

namespace BirimizKovulacak.Voice
{
    /// <summary>Host-side: where each player's avatar is and whether the slot takes part in voice.</summary>
    public interface IVoicePositions
    {
        /// <summary>True for an occupied, connected slot with a real (human) client that can speak/listen.</summary>
        bool IsActive(int slot);
        bool TryGetPosition(int slot, out VoiceVec3 position);
    }

    /// <summary>Host-side: the connected phone-call partner of a player, or -1.</summary>
    public interface IVoiceCallState
    {
        int GetCallPartner(int slot);
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

        public static readonly VoiceRoutingSettings Default = new VoiceRoutingSettings();
    }

    /// <summary>
    /// Default host routing rules (pure, unit-tested):
    /// <list type="bullet">
    /// <item>Muted: nothing. Inactive speaker or CanSpeak=false: nothing.</item>
    /// <item>AllHear: every other active slot, flag AllHear (2D, full volume).</item>
    /// <item>Office: active slots within MaxDistance (flag Muffled when occluded, cut at MuffledMaxDistance);
    /// plus the call partner with flag Call (clear, 2D) regardless of distance. A call stream never goes to
    /// anyone else — bystanders only hear the speaker's physical voice through proximity (eavesdropping).</item>
    /// </list>
    /// </summary>
    public sealed class ProximityVoiceRouter : IVoiceRouter
    {
        public IVoicePositions Positions;
        public IVoiceCallState Calls;
        public IVoiceModeProvider Mode;
        public IVoiceOcclusion Occlusion;
        public VoiceRoutingSettings Settings;
        public int SlotCount = VoiceFormat.MaxPlayers;

        // Counters (cumulative).
        public long Decisions, Delivered, CallRoutes, MuffledRoutes, MutedDrops, OutOfRange;

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

            // Office: private call first.
            int partner = Calls != null ? Calls.GetCallPartner(speaker) : -1;
            if (partner < 0 || partner >= SlotCount || partner == speaker || !Positions.IsActive(partner) ||
                (Settings.RequireMutualCall && Calls.GetCallPartner(partner) != speaker))
                partner = -1;
            if (partner >= 0 && n < routes.Length)
            {
                routes[n].Recipient = partner;
                routes[n].Flags = VoiceFrameFlags.Call;
                n++;
                CallRoutes++;
            }

            VoiceVec3 sp;
            if (Positions.TryGetPosition(speaker, out sp))
            {
                float max2 = Settings.MaxDistance * Settings.MaxDistance;
                float muffledMax2 = Settings.MuffledMaxDistance * Settings.MuffledMaxDistance;
                for (int r = 0; r < SlotCount && n < routes.Length; r++)
                {
                    if (r == speaker || r == partner || !Positions.IsActive(r)) continue;
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
            Delivered += n;
            return n;
        }
    }
}
