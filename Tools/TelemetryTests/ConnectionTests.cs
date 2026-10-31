using System;
using System.Linq;
using BirimizKovulacak.Core;
using NUnit.Framework;

namespace BirimizKovulacak.Telemetry.Tests
{
    public class ConnectionTests
    {
        sealed class Rig
        {
            public Harness H = new Harness();
            public ClientConnectionTelemetry C;
            public Rig() { C = new ClientConnectionTelemetry(H.Service, () => H.Clock, 6.0); }
            public void S(ClientConnState s, string err = "", LinePhase phase = LinePhase.None, bool mid = false, double dt = 0)
            {
                H.Clock += dt;
                C.OnState(s, err, phase, mid);
            }
            public string[] Names { get { return H.Flushed().Select(e => e.Name).ToArray(); } }
            public TelemetryEvent Ev(string n) { return H.Flushed().Single(e => e.Name == n); }
        }

        [Test]
        public void Join_Success_ThenUserLeaves()
        {
            var r = new Rig();
            r.S(ClientConnState.Connecting);
            r.S(ClientConnState.Connected, dt: 1.5);
            r.S(ClientConnState.Offline, dt: 20);
            CollectionAssert.AreEqual(new[] { "lobby_join_attempted", "lobby_joined", "lobby_left" }, r.Names);
            Assert.AreEqual("manual", r.Ev("lobby_join_attempted").GetString("kind"));
            Assert.AreEqual(1.5, r.Ev("lobby_joined").GetFloat("wait_seconds"), 1e-6);
            Assert.AreEqual("user", r.Ev("lobby_left").GetString("reason"));
            Assert.AreEqual(20.0, r.Ev("lobby_left").GetFloat("seconds_in_lobby"), 1e-6);
        }

        [Test]
        public void Join_Rejected_IsReportedOnce()
        {
            var r = new Rig();
            r.C.NoteJoinKind("auto");
            r.S(ClientConnState.Connecting);
            r.S(ClientConnState.Failed, "reject.full", dt: 0.4);
            r.H.Clock += 30; r.C.Tick();
            CollectionAssert.AreEqual(new[] { "lobby_join_attempted", "lobby_join_failed" }, r.Names);
            Assert.AreEqual("auto", r.Ev("lobby_join_attempted").GetString("kind"));
            Assert.AreEqual("reject.full", r.Ev("lobby_join_failed").GetString("reason"));
        }

        [Test]
        public void Reconnect_FlipFlop_EmitsOnlyTheTerminalOutcome()
        {
            var r = new Rig();
            r.S(ClientConnState.Connecting); r.S(ClientConnState.Connected, dt: 1);
            r.S(ClientConnState.Reconnecting, "net.reconnecting", LinePhase.RoundPlay, true, 40);
            // the reconnect loop: a rejection flips the state to Failed and the next attempt flips it back
            r.S(ClientConnState.Failed, "reject.in_progress", LinePhase.None, true, 1);
            r.S(ClientConnState.Connecting, dt: 4.5);
            r.S(ClientConnState.Failed, "reject.in_progress", LinePhase.None, true, 0.5);
            r.S(ClientConnState.Connecting, dt: 4.5);
            Assert.AreEqual(0, r.H.Flushed().Count(e => e.Name == "lobby_join_failed" || e.Name == "reconnect_failed" || e.Name == "session_interrupted"), "no terminal event while the loop is retrying");
            r.S(ClientConnState.Failed, "net.reconnect_failed", LinePhase.None, true, 20);
            var names = r.Names;
            Assert.AreEqual(1, names.Count(n => n == "reconnect_failed"));
            Assert.AreEqual(1, names.Count(n => n == "lobby_join_failed"));
            Assert.AreEqual(1, names.Count(n => n == "session_interrupted"));
            Assert.AreEqual(1, names.Count(n => n == "reconnect_attempted"));
            Assert.AreEqual("reject.in_progress", r.Ev("lobby_join_failed").GetString("reason"), "the most specific reason wins over the generic loop end");
            Assert.AreEqual("reconnect_failed", r.Ev("session_interrupted").GetString("reason"));
            Assert.AreEqual("round_play", r.Ev("session_interrupted").GetString("phase"));
            Assert.IsTrue(r.Ev("session_interrupted").GetBool("mid_match"));
            Assert.AreEqual(3, r.Ev("reconnect_failed").GetInt("attempt"));
        }

        [Test]
        public void Reconnect_FlipFlopThenSuccess_EmitsNoFailure()
        {
            var r = new Rig();
            r.S(ClientConnState.Connecting); r.S(ClientConnState.Connected, dt: 1);
            r.S(ClientConnState.Reconnecting, "net.reconnecting", LinePhase.RoundPlay, true, 10);
            r.S(ClientConnState.Failed, "reject.in_progress", LinePhase.None, true, 1);
            r.S(ClientConnState.Connecting, dt: 4.5);
            r.S(ClientConnState.Connected, dt: 1);
            var names = r.Names;
            Assert.AreEqual(0, names.Count(n => n.Contains("failed") || n == "session_interrupted"));
            Assert.AreEqual(1, names.Count(n => n == "reconnect_succeeded"));
            Assert.AreEqual(6.5, r.Ev("reconnect_succeeded").GetFloat("seconds"), 1e-6);
            Assert.AreEqual(2, r.H.Flushed().Count(e => e.Name == "lobby_joined"));
            Assert.IsTrue(r.H.Flushed().Last(e => e.Name == "lobby_joined").GetBool("reconnect"));
        }

        [Test]
        public void HeldFailure_IsReportedAfterTheHoldWhenNoRetryFollows()
        {
            var r = new Rig();
            r.S(ClientConnState.Connecting); r.S(ClientConnState.Connected, dt: 1);
            r.S(ClientConnState.Reconnecting, "net.reconnecting", LinePhase.Morning, true, 5);
            r.S(ClientConnState.Failed, "reject.version", LinePhase.None, true, 1);
            Assert.IsTrue(r.C.HasPendingFailure);
            r.H.Clock += 3; r.C.Tick();
            Assert.IsTrue(r.C.HasPendingFailure, "still inside the hold");
            r.H.Clock += 4; r.C.Tick();
            Assert.IsFalse(r.C.HasPendingFailure);
            Assert.AreEqual("incompatible", r.Ev("session_interrupted").GetString("reason"));
        }

        [Test]
        public void HostLostWithoutReconnectLoop_IsAnInterruption()
        {
            var r = new Rig();
            r.S(ClientConnState.Connecting); r.S(ClientConnState.Connected, dt: 1);
            r.S(ClientConnState.Failed, "net.host_lost", LinePhase.None, false, 60);
            CollectionAssert.AreEqual(new[] { "lobby_join_attempted", "lobby_joined", "session_interrupted", "host_departed", "lobby_left" }, r.Names);
            Assert.AreEqual("host_lost", r.Ev("session_interrupted").GetString("reason"));
            Assert.IsFalse(r.Ev("session_interrupted").GetBool("mid_match"));
            Assert.IsFalse(r.Ev("host_departed").GetBool("graceful"));
            Assert.AreEqual("error", r.Ev("lobby_left").GetString("reason"));
        }

        [Test]
        public void HostLostAfterResults_IsGraceful()
        {
            var r = new Rig();
            r.S(ClientConnState.Connecting); r.S(ClientConnState.Connected, dt: 1);
            r.S(ClientConnState.Failed, "net.host_lost", LinePhase.WeekResults, true, 60);
            Assert.IsTrue(r.Ev("host_departed").GetBool("graceful"));
            Assert.AreEqual("host_closed", r.Ev("lobby_left").GetString("reason"));
        }

        [Test]
        public void InterruptionReasonCategories()
        {
            Assert.AreEqual("incompatible", ClientConnectionTelemetry.InterruptionReason("reject.version", false));
            Assert.AreEqual("incompatible", ClientConnectionTelemetry.InterruptionReason("net.incompatible", false));
            Assert.AreEqual("kicked", ClientConnectionTelemetry.InterruptionReason("reject.kicked", false));
            Assert.AreEqual("transport_failed", ClientConnectionTelemetry.InterruptionReason("net.transport_failed", false));
            Assert.AreEqual("reconnect_failed", ClientConnectionTelemetry.InterruptionReason("net.reconnect_failed", false));
            Assert.AreEqual("reconnect_failed", ClientConnectionTelemetry.InterruptionReason("reject.full", true));
            Assert.AreEqual("host_lost", ClientConnectionTelemetry.InterruptionReason("net.host_lost", false));
            int i;
            foreach (var d in new[] { "host_lost", "reconnect_failed", "transport_failed", "incompatible", "kicked" })
                Assert.IsTrue(TelemetryCatalog.Get("session_interrupted").Find("reason", out i).AllowsEnum(d));
        }

        [Test]
        public void NoEventsBeforeConsent()
        {
            var r = new Rig();
            r.H.Service.SetLocalConsent(0);
            r.S(ClientConnState.Connecting); r.S(ClientConnState.Connected, dt: 1); r.S(ClientConnState.Offline, dt: 3);
            Assert.AreEqual(0, r.H.Flushed().Count);
        }

        [Test]
        public void HostSideFailureIsNotAJoinOutcome()
        {
            var r = new Rig();
            r.S(ClientConnState.Failed, "net.host_failed");
            r.H.Clock += 20; r.C.Tick();
            Assert.AreEqual(0, r.H.Flushed().Count);
        }
    }
}
