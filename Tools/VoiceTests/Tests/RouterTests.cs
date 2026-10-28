using System.Collections.Generic;
using BirimizKovulacak.Voice;
using NUnit.Framework;

namespace BirimizKovulacak.VoiceTests
{
    [TestFixture]
    public class RouterTests
    {
        readonly VoiceRoute[] _routes = new VoiceRoute[VoiceFormat.MaxPlayers];

        Dictionary<int, VoiceFrameFlags> Route(ProximityVoiceRouter router, int speaker)
        {
            int n = router.Route(speaker, _routes);
            var d = new Dictionary<int, VoiceFrameFlags>();
            for (int i = 0; i < n; i++)
            {
                Assert.IsFalse(d.ContainsKey(_routes[i].Recipient), "each recipient at most once");
                d[_routes[i].Recipient] = _routes[i].Flags;
            }
            Assert.IsFalse(d.ContainsKey(speaker), "never back to the speaker");
            return d;
        }

        static ProximityVoiceRouter Router(FakeWorld w) { return new ProximityVoiceRouter(w, w, w, w); }

        [Test]
        public void Office_SendsWithinMaxDistanceOnly()
        {
            var w = new FakeWorld().Place(0, 0, 0).Place(1, 13.9f, 0).Place(2, 14.1f, 0).Place(3, 0, 2);
            var r = Route(Router(w), 0);
            CollectionAssert.AreEquivalent(new[] { 1, 3 }, r.Keys);
            Assert.AreEqual(VoiceFrameFlags.None, r[1]);
            Assert.AreEqual(VoiceFrameFlags.None, r[3]);
        }

        [Test]
        public void Office_WallBetween_SetsMuffled()
        {
            var w = new FakeWorld().Place(0, 0, 0).Place(1, 5, 0).Place(2, -3, 0);
            w.WallX = 2f; // wall between 0 and 1, not between 0 and 2
            var r = Route(Router(w), 0);
            Assert.AreEqual(VoiceFrameFlags.Muffled, r[1]);
            Assert.AreEqual(VoiceFrameFlags.None, r[2]);
        }

        [Test]
        public void Office_MuffledRangeCanBeShorter()
        {
            var w = new FakeWorld().Place(0, 0, 0).Place(1, 10, 0);
            w.WallX = 2f;
            var router = Router(w);
            router.Settings = new VoiceRoutingSettings { MuffledMaxDistance = 8f };
            Assert.AreEqual(0, Route(router, 0).Count, "beyond the muffled range through a wall");
            router.Settings.MuffledMaxDistance = 14f;
            Assert.AreEqual(VoiceFrameFlags.Muffled, Route(router, 0)[1]);
        }

        [Test]
        public void Call_ReachesPartnerAnywhere_AndNobodyElse()
        {
            // 0 calls 1 across the office. 2 stands next to 1 (far from 0). 3 stands next to 0 (eavesdropper).
            var w = new FakeWorld().Place(0, -15, -8).Place(1, 15, 8).Place(2, 15.5f, 8).Place(3, -14, -8);
            w.Call(0, 1);
            var router = Router(w);

            var from0 = Route(router, 0);
            Assert.AreEqual(VoiceFrameFlags.Call, from0[1], "partner hears the call clearly");
            Assert.IsFalse(from0.ContainsKey(2), "third party near the listener never gets the call stream");
            Assert.AreEqual(VoiceFrameFlags.None, from0[3], "people near the speaker hear the physical voice");

            var from1 = Route(router, 1);
            Assert.AreEqual(VoiceFrameFlags.Call, from1[0]);
            Assert.AreEqual(VoiceFrameFlags.None, from1[2], "1's physical voice reaches 2 (eavesdropping)");
            Assert.IsFalse(from1.ContainsKey(3), "3 is far from 1 and not in the call");

            // A bystander's own voice is plain proximity, never a call.
            var from2 = Route(router, 2);
            CollectionAssert.AreEquivalent(new[] { 1 }, from2.Keys);
            Assert.AreEqual(VoiceFrameFlags.None, from2[1]);
        }

        [Test]
        public void Call_PartnerNearby_GetsSingleCallRoute()
        {
            var w = new FakeWorld().Place(0, 0, 0).Place(1, 1, 0);
            w.Call(0, 1);
            var r = Route(Router(w), 0);
            Assert.AreEqual(1, r.Count);
            Assert.AreEqual(VoiceFrameFlags.Call, r[1]);
        }

        [Test]
        public void Call_RequiresMutualPartners()
        {
            var w = new FakeWorld().Place(0, -15, 0).Place(1, 15, 0);
            w.Partner[0] = 1; // ringing / one-sided state: 1 has not answered
            Assert.AreEqual(0, Route(Router(w), 0).Count);
            var router = Router(w);
            router.Settings = new VoiceRoutingSettings { RequireMutualCall = false };
            Assert.AreEqual(VoiceFrameFlags.Call, Route(router, 0)[1]);
        }

        [Test]
        public void AllHear_SendsToEveryActivePlayer()
        {
            var w = new FakeWorld().Place(0, -15, -8).Place(1, 15, 8).Place(2, 0, 0).Place(4, 3, 3);
            w.Mode = VoiceMode.AllHear;
            w.WallX = 1f;
            w.Call(0, 1);
            var r = Route(Router(w), 0);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 4 }, r.Keys);
            foreach (var kv in r) Assert.AreEqual(VoiceFrameFlags.AllHear, kv.Value);
        }

        [Test]
        public void Muted_SendsNothing()
        {
            var w = new FakeWorld().Place(0, 0, 0).Place(1, 1, 0);
            w.Call(0, 1);
            w.Mode = VoiceMode.Muted;
            var router = Router(w);
            Assert.AreEqual(0, router.Route(0, _routes));
            Assert.AreEqual(1, router.MutedDrops);
        }

        [Test]
        public void InactiveSlots_NeitherSpeakNorListen()
        {
            var w = new FakeWorld().Place(0, 0, 0).Place(1, 1, 0).Place(2, 2, 0);
            w.Active[2] = false; // disconnected / host-side bot
            CollectionAssert.AreEquivalent(new[] { 1 }, Route(Router(w), 0).Keys);
            Assert.AreEqual(0, Route(Router(w), 2).Count);
            w.Mode = VoiceMode.AllHear;
            CollectionAssert.AreEquivalent(new[] { 1 }, Route(Router(w), 0).Keys);
        }

        [Test]
        public void CanSpeakFalse_SilencesSpeaker()
        {
            var w = new FakeWorld().Place(0, 0, 0).Place(1, 1, 0);
            w.Mode = VoiceMode.AllHear;
            w.Muted[0] = true;
            Assert.AreEqual(0, Route(Router(w), 0).Count);
            Assert.AreEqual(1, Route(Router(w), 1).Count);
        }

        [Test]
        public void MissingPosition_SkipsProximityButKeepsCall()
        {
            var w = new FakeWorld().Place(0, 0, 0).Place(1, 1, 0).Place(2, 2, 0);
            w.HasPos[0] = false; // avatar not spawned yet
            w.Call(0, 2);
            var r = Route(Router(w), 0);
            CollectionAssert.AreEquivalent(new[] { 2 }, r.Keys);
            Assert.AreEqual(VoiceFrameFlags.Call, r[2]);
        }

        [Test]
        public void OcclusionIsOnlyQueriedInRange()
        {
            var w = new FakeWorld().Place(0, 0, 0).Place(1, 30, 0).Place(2, 1, 0);
            w.WallX = 0.5f;
            Route(Router(w), 0);
            Assert.AreEqual(1, w.OcclusionQueries, "no linecast for players beyond the max distance");
        }

        [Test]
        public void Attenuation_FullNearSilentFar()
        {
            Assert.AreEqual(1f, VoiceAttenuation.Gain(0f, 3, 14));
            Assert.AreEqual(1f, VoiceAttenuation.Gain(3f, 3, 14));
            Assert.AreEqual(0f, VoiceAttenuation.Gain(14f, 3, 14));
            Assert.AreEqual(0f, VoiceAttenuation.Gain(20f, 3, 14));
            float prev = 1f;
            for (float d = 3.5f; d < 14; d += 0.5f)
            {
                float g = VoiceAttenuation.Gain(d, 3, 14);
                Assert.Less(g, prev, "monotonic falloff at " + d);
                prev = g;
            }
            Assert.That(VoiceAttenuation.Gain(7f, 3, 14), Is.InRange(0.2f, 0.4f));
        }
    }
}
