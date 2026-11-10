using System;
using System.Collections.Generic;
using System.Linq;
using BirimizKovulacak.Core;
using NUnit.Framework;

namespace BirimizKovulacak.Telemetry.Tests
{
    /// <summary>Regressions from the independent review of the integrated v4 branch (service-level ones; the v4 observer regressions H2, H4, H5, M9, M10, M12 left with the observer in v5 Phase 1).</summary>
    public class RegressionTests
    {

        // ---- H3 ----------------------------------------------------------------------------------

        sealed class HookSink : ITelemetrySink
        {
            public Action OnWrite;
            public int Withdrawn;
            public readonly List<TelemetryEvent> Events = new List<TelemetryEvent>();
            public TelemetrySinkKind Kind { get; set; }
            public string Name { get { return "other"; } }
            public void Write(IReadOnlyList<TelemetryEvent> b) { Events.AddRange(b); if (OnWrite != null) OnWrite(); }
            public void Flush() { }
            public void Dispose() { }
            public void OnConsentWithdrawn() { Withdrawn++; }
        }

        static bool CloudEvent(Harness h) { return h.Service.Begin("session_ended").Float("duration_seconds", 5).Enum("reason", "quit").Emit(); }

        [Test]
        public void H3_ConsentLoweredAfterAcceptanceRevokesTheCloudCopy()
        {
            var h = new Harness(null, 3);
            var cloud = new HookSink { Kind = TelemetrySinkKind.Cloud };
            h.Service.AddSink(cloud);
            Assert.IsTrue(CloudEvent(h));
            h.Service.SetLocalConsent(2);
            h.Service.Flush();
            Assert.AreEqual(0, cloud.Events.Count, "accepted at consent 3 but delivered at consent 2");
            Assert.AreEqual(1, h.Sink.Count("session_ended"), "the local copy is still allowed");
            Assert.AreEqual(1, cloud.Withdrawn, "cloud sinks are told to drop their own queues");
        }

        [Test]
        public void H3_WithdrawalWhileDeliveringStopsLaterSinks()
        {
            var h = new Harness(null, 3);
            var first = new HookSink { Kind = TelemetrySinkKind.Local };
            var second = new HookSink { Kind = TelemetrySinkKind.Local };
            first.OnWrite = () => h.Service.SetLocalConsent(1); // the player withdraws while the batch is being delivered
            h.Service.AddSink(first); h.Service.AddSink(second);
            CloudEvent(h);
            h.Service.Flush();
            Assert.AreEqual(1, first.Events.Count);
            Assert.AreEqual(0, second.Events.Count, "events detached before the withdrawal must not reach a later sink");
            Assert.AreEqual(1, h.Sink.Count("session_ended"), "the capture sink (registered first) was delivered before the withdrawal");
        }

        [Test]
        public void H3_ParticipantWithdrawalInvalidatesDetachedBatch()
        {
            var h = new Harness(null, 3, true);
            h.Service.Context.BeginMatch("m-h3", 1, 2, false, null);
            h.Service.SetParticipant(1, 3, "p-00000000000000a1", false);
            var a = new HookSink { Kind = TelemetrySinkKind.Local };
            var b = new HookSink { Kind = TelemetrySinkKind.Local };
            a.OnWrite = () => h.Service.SetParticipant(1, 1, "", false);
            h.Service.AddSink(a); h.Service.AddSink(b);
            h.Service.Begin("fault_points_changed").Slot("player", 1).Int("delta", 1).Enum("reason", "vote").Int("blame_after", 6).Emit();
            h.Service.Flush();
            Assert.AreEqual(1, a.Events.Count);
            Assert.AreEqual(0, b.Events.Count);
        }

        // ---- M11 ---------------------------------------------------------------------------------

        [Test]
        public void M11_LobbyEventIdsDifferAcrossHostingSessions()
        {
            string Id(string session)
            {
                var h = new Harness(null, 3, true);
                h.Service.Context.AdoptSession(session, "");
                h.Service.Begin("match_start_attempted").Int("humans", 3).Int("bots", 0).Int("ready", 3).Emit();
                return h.Flushed()[0].EventId;
            }
            Assert.AreNotEqual(Id("s-aaaaaaaa"), Id("s-bbbbbbbb"));
            Assert.AreEqual(Id("s-aaaaaaaa"), Id("s-aaaaaaaa"));
        }

        // ---- M13 ---------------------------------------------------------------------------------

        [Test]
        public void M13_HostModeEndsWhenHostingEnds()
        {
            var h = new Harness(null, 3, true);
            var c = new ClientConnectionTelemetry(h.Service, () => h.Clock);
            h.Service.Context.BeginMatch("m-old", 1, 4, false, null);
            c.OnState(ClientConnState.Hosting, "", LinePhase.None, false);
            Assert.IsTrue(h.Service.HostMode);
            c.OnState(ClientConnState.Offline, "", LinePhase.None, false);
            Assert.IsFalse(h.Service.HostMode, "a later join must be treated as a client again");
            Assert.AreEqual("", h.Service.Context.MatchId);
        }

        // ---- M14 ---------------------------------------------------------------------------------

        [Test]
        public void M14_ReleasePrivateRespectsTheQueueBound()
        {
            var h = new Harness(c => c.QueueBound = 16, 3, true);
            h.Service.Context.BeginMatch("m-q", 1, 2, false, null);
            h.Service.SetParticipant(0, 3, "p-00000000000000a0", false);
            for (int round = 0; round < 6; round++)
            {
                for (int i = 0; i < 10; i++)
                    h.Service.Begin("income_credited").Slot("player", 0).Int("amount", 1).Int("raw_amount", 1).Enum("reason", "task_income").Int("block_total", round * 10 + i).Emit();
                h.Service.ReleasePrivate(); // repeated match ends while the sink never gets to flush
                Assert.LessOrEqual(h.Service.GetStats().QueueSize, 16);
            }
            Assert.Greater(h.Service.GetStats().DroppedOverflow, 0);
            var ev = h.Flushed();
            Assert.AreEqual(1, ev.Count(e => e.Name == "telemetry_queue_dropped" && e.GetString("reason") == "overflow"));
        }
    }
}
