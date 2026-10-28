using System;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// A client's request. The host never trusts any outcome claimed by a client: it validates phase, match id,
    /// proximity, carried item, permissions, cooldowns and resources, then either starts a timed interaction,
    /// applies an instant change, or rejects with a <see cref="FailureCode"/>.
    /// </summary>
    [Serializable]
    public sealed class ActionRequest
    {
        /// <summary>Must equal the current match id; stale requests from a previous match are rejected.</summary>
        public int MatchId;
        /// <summary>Client-local monotonically increasing id; the host ignores duplicates per player.</summary>
        public uint RequestId;
        public ActionVerb Verb;
        public string TargetId = "";
        public int IntParam = -1;

        public ActionRequest() { }

        public ActionRequest(int matchId, uint requestId, ActionVerb verb, string targetId = "", int intParam = -1)
        {
            MatchId = matchId;
            RequestId = requestId;
            Verb = verb;
            TargetId = targetId ?? "";
            IntParam = intParam;
        }

        public override string ToString() { return Verb + "(" + TargetId + "," + IntParam + ")#" + RequestId; }
    }

    /// <summary>
    /// Immediate answer to a request. Accepted means "applied" for instant verbs and "started" for timed ones;
    /// completion of a timed interaction is reported through state (and an InteractionCompleted event).
    /// </summary>
    [Serializable]
    public sealed class ActionResult
    {
        public uint RequestId;
        public bool Accepted;
        public FailureCode Failure;
        /// <summary>For accepted timed interactions: host time at which it will complete.</summary>
        public double CompletesAt;

        public static ActionResult Ok(uint requestId, double completesAt = 0) { return new ActionResult { RequestId = requestId, Accepted = true, Failure = FailureCode.None, CompletesAt = completesAt }; }
        public static ActionResult Fail(uint requestId, FailureCode code) { return new ActionResult { RequestId = requestId, Accepted = false, Failure = code }; }
        public override string ToString() { return Accepted ? ("OK#" + RequestId) : ("FAIL#" + RequestId + ":" + Failure); }
    }

    /// <summary>One contextual option for the interaction prompt (computed client-side from the player's own view).</summary>
    [Serializable]
    public sealed class Affordance
    {
        public ActionVerb Verb;
        public string TargetId = "";
        public int IntParam = -1;
        /// <summary>Expected duration in seconds (0 = instant).</summary>
        public float Seconds;
        /// <summary>Localization key for the prompt, e.g. "verb.brew_coffee".</summary>
        public string LabelKey = "";
        /// <summary>True when completing it would create a sabotage incident (UI shows a subtle marker to the actor only).</summary>
        public bool IsSabotage;
        /// <summary>If not None, the option is shown greyed out with this reason.</summary>
        public FailureCode BlockedBy;
    }

    /// <summary>Avatar kinematics sampled by the host from the network transform each tick.</summary>
    public struct AvatarSample
    {
        public Vec3 Position;
        public float Yaw;
        public float Speed;
        /// <summary>v2: sneaking (crouch walk). Witnesses notice a crouched actor only within a shorter range.</summary>
        public bool Crouched;
    }
}
