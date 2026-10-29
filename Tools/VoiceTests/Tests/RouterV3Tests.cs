using System.Collections.Generic;
using BirimizKovulacak.Voice;
using NUnit.Framework;

namespace BirimizKovulacak.VoiceTests
{
    /// <summary>v3 routing: calls only during the day, wiretaps, meeting all-hear, the defense floor.</summary>
    [TestFixture]
    public class RouterV3Tests
    {
        readonly VoiceRoute[] _routes = new VoiceRoute[VoiceFormat.MaxPlayers];
        const VoiceFrameFlags TapFlags = VoiceFrameFlags.Call | VoiceFrameFlags.Tap;

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

        /// <summary>Six players in their own offices: active, but the v3 provider knows no positions.</summary>
        static FakeWorld Offices(int players = 6)
        {
            var w = new FakeWorld { Mode = VoiceMode.Calls };
            for (int i = 0; i < players; i++) { w.Active[i] = true; w.HasPos[i] = false; }
            return w;
        }

        static ProximityVoiceRouter Router(FakeWorld w) { return new ProximityVoiceRouter(w, w, w, w); }

        [Test]
        public void Calls_NoProximityEvenSideBySide()
        {
            // Positions known and everyone in one spot: Calls mode still sends nothing without a call.
            var w = new FakeWorld().Place(0, 0, 0).Place(1, 0.5f, 0).Place(2, 1, 0);
            w.Mode = VoiceMode.Calls;
            var router = Router(w);
            Assert.AreEqual(0, Route(router, 0).Count);
            Assert.AreEqual(0, w.OcclusionQueries, "no wall tests in Calls mode");
            Assert.AreEqual(-1, router.LastCallId);
        }

        [Test]
        public void Calls_OnlyTheConnectedPartner_NoPositionsNeeded()
        {
            var w = Offices();
            w.Call(0, 3);
            var router = Router(w);
            var r = Route(router, 0);
            CollectionAssert.AreEquivalent(new[] { 3 }, r.Keys);
            Assert.AreEqual(VoiceFrameFlags.Call, r[3]);
            Assert.AreEqual(w.CallIds[0], router.LastCallId, "call frame attributed to the call");
            Assert.AreEqual(3, router.LastCallPartner);
            var back = Route(router, 3);
            CollectionAssert.AreEquivalent(new[] { 0 }, back.Keys);
            Assert.AreEqual(0, Route(router, 1).Count, "players outside calls are heard by nobody");
            Assert.AreEqual(-1, router.LastCallId);
        }

        [Test]
        public void Calls_MutualConnectionRequired_RingingIsSilent()
        {
            var w = Offices();
            w.Partner[0] = 1; // ringing / one-sided
            w.CallIds[0] = 5;
            w.Tap(4, 0);
            var router = Router(w);
            Assert.AreEqual(0, Route(router, 0).Count, "nobody hears a ringing caller, not even a tapper");
            Assert.AreEqual(0, router.TapRoutes);
            Assert.AreEqual(-1, router.LastCallId);
        }

        [Test]
        public void Calls_MismatchedCallIds_RouteButDoNotRecord()
        {
            var w = Offices();
            w.Call(0, 1);
            w.CallIds[1] = 999; // provider bug / race: ids disagree
            var router = Router(w);
            Assert.AreEqual(VoiceFrameFlags.Call, Route(router, 0)[1]);
            Assert.AreEqual(-1, router.LastCallId, "not attributed to any recording");
        }

        [Test]
        public void Tap_HearsBothSidesOfTheTargetsCall()
        {
            var w = Offices();
            w.Call(0, 1);
            w.Tap(4, 0); // 4 wiretaps 0
            var router = Router(w);
            var from0 = Route(router, 0);
            CollectionAssert.AreEquivalent(new[] { 1, 4 }, from0.Keys);
            Assert.AreEqual(VoiceFrameFlags.Call, from0[1]);
            Assert.AreEqual(TapFlags, from0[4], "tap frames are flagged so the client can mix them");
            var from1 = Route(router, 1);
            CollectionAssert.AreEquivalent(new[] { 0, 4 }, from1.Keys);
            Assert.AreEqual(TapFlags, from1[4], "the tapper also hears the other side of the call");
            Assert.AreEqual(2, router.TapRoutes);
            Assert.AreEqual(w.CallIds[0], router.LastCallId, "tapped frames are still normal call frames for the recorder");
        }

        [Test]
        public void Tap_OnlyWhileTheCallIsConnected()
        {
            var w = Offices();
            w.Tap(4, 0);
            var router = Router(w);
            Assert.AreEqual(0, Route(router, 0).Count, "target not in a call: nothing to hear");
            w.Call(0, 2);
            Assert.AreEqual(TapFlags, Route(router, 0)[4]);
            w.HangUp(0);
            Assert.AreEqual(0, Route(router, 0).Count, "hung up: the tap goes silent");
            Assert.AreEqual(0, Route(router, 2).Count);
        }

        [Test]
        public void Tap_IsSilent_TheTappersVoiceReachesNobodyThroughIt()
        {
            var w = Offices();
            w.Call(0, 1);
            w.Tap(4, 0);
            var router = Router(w);
            Assert.AreEqual(0, Route(router, 4).Count, "tapper not in a call: its voice goes nowhere");
            // The tapper takes its own call with 5: its voice reaches 5 only — never 0 or 1.
            w.Call(4, 5);
            var from4 = Route(router, 4);
            CollectionAssert.AreEquivalent(new[] { 5 }, from4.Keys);
            Assert.AreEqual(VoiceFrameFlags.Call, from4[5]);
            // ...and it still hears the tapped call meanwhile.
            Assert.AreEqual(TapFlags, Route(router, 1)[4]);
        }

        [Test]
        public void Tap_PartnerOrDoubleTapGetsOneRoute_InactiveTapperSkipped()
        {
            var w = Offices();
            w.Call(0, 1);
            w.Tap(1, 0);             // the partner "taps" its own call: keeps the plain call route
            w.Tap(3, 0); w.Tap(3, 1); // taps both sides: one route
            w.Tap(5, 1);
            w.Active[5] = false;     // disconnected tapper
            var r = Route(Router(w), 0);
            CollectionAssert.AreEquivalent(new[] { 1, 3 }, r.Keys);
            Assert.AreEqual(VoiceFrameFlags.Call, r[1]);
            Assert.AreEqual(TapFlags, r[3]);
        }

        [Test]
        public void Tap_TwoTappersOnDifferentSides()
        {
            var w = Offices();
            w.Call(2, 3);
            w.Tap(0, 2);
            w.Tap(5, 3);
            var r = Route(Router(w), 2);
            CollectionAssert.AreEquivalent(new[] { 3, 0, 5 }, r.Keys);
            Assert.AreEqual(TapFlags, r[0]);
            Assert.AreEqual(TapFlags, r[5]);
        }

        [Test]
        public void Office_TapperNearbyIsNotDoubledByProximity()
        {
            var w = new FakeWorld().Place(0, 0, 0).Place(1, 30, 0).Place(2, 1, 0);
            w.Call(0, 1);
            w.Tap(2, 0);
            var r = Route(Router(w), 0);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, r.Keys);
            Assert.AreEqual(TapFlags, r[2], "tap route wins; no second proximity route");
        }

        [Test]
        public void AllHear_Meeting_EveryoneHearsEveryone_NoCallsNoTaps()
        {
            var w = Offices(5);
            w.Mode = VoiceMode.AllHear;
            w.Call(0, 1);
            w.Tap(4, 0);
            var router = Router(w);
            var r = Route(router, 0);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4 }, r.Keys);
            foreach (var kv in r) Assert.AreEqual(VoiceFrameFlags.AllHear, kv.Value);
            Assert.AreEqual(-1, router.LastCallId, "meeting voice is never recorded as a call");
            Assert.AreEqual(0, router.TapRoutes);
            Assert.AreEqual(0, w.TapperQueries, "no tap lookups outside the day");
        }

        [Test]
        public void FinalDefense_OnlyTheDefenderSpeaks()
        {
            var w = Offices(4);
            w.Mode = VoiceMode.AllHear;
            int defender = 2;
            for (int i = 0; i < 4; i++) w.Muted[i] = i != defender; // CanSpeak(slot) = slot == defender
            var router = Router(w);
            for (int i = 0; i < 4; i++)
            {
                var r = Route(router, i);
                if (i == defender) CollectionAssert.AreEquivalent(new[] { 0, 1, 3 }, r.Keys, "everybody hears the defender");
                else Assert.AreEqual(0, r.Count, "slot " + i + " is silent during the defense");
            }
            Assert.AreEqual(3, router.MutedDrops);
        }

        [Test]
        public void Muted_SilencesCallsAndTaps()
        {
            var w = Offices();
            w.Call(0, 1);
            w.Tap(3, 0);
            w.Mode = VoiceMode.Muted;
            Assert.AreEqual(0, Route(Router(w), 0).Count);
        }

        [Test]
        public void Route_IsAllocationFree_WithTaps()
        {
            var w = Offices();
            w.Call(0, 1);
            w.Tap(2, 0);
            w.Tap(3, 1);
            var router = Router(w);
            void Burst() { for (int i = 0; i < 10000; i++) { router.Route(0, _routes); router.Route(1, _routes); router.Route(4, _routes); } }
            Burst(); // warm-up
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            Burst();
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before; // read before Assert boxes its arguments
            Assert.AreEqual(0, allocated);
        }
    }
}
