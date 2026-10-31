using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BirimizKovulacak.Core;
using NUnit.Framework;

namespace BirimizKovulacak.Telemetry.Tests
{
    public class ServiceTests
    {
        static bool Phase(Harness h, string reason = "timer")
        {
            return h.Service.Begin("phase_exited").Float("duration_seconds", 5).Enum("completion_reason", reason).Emit();
        }

        // ---- context / ids -----------------------------------------------------------------------

        [Test]
        public void SessionContext_InitializesOnce()
        {
            var ctx = new TelemetrySessionContext();
            Assert.IsTrue(ctx.InitializeOnce("playtest", "s-aaaa", "G07", "friends_wave1", new[] { "rec_select=context" }));
            Assert.IsFalse(ctx.InitializeOnce("production", "s-bbbb", "G08", "other", null));
            Assert.AreEqual("s-aaaa", ctx.SessionId);
            Assert.AreEqual("playtest", ctx.Environment);
            Assert.AreEqual("G07", ctx.GroupId);
            Assert.AreEqual(1, ctx.Initializations);
            var h = new Harness();
            string id = h.Service.Context.SessionId;
            StringAssert.StartsWith("s-", id);
            Assert.AreEqual(34, id.Length);
            h.Service.Context.InitializeOnce("x", "s-other", "", "", null);
            Assert.AreEqual(id, h.Service.Context.SessionId, "service already initialised the context");
        }

        [Test]
        public void MatchAndWeekIds_UpdateAndClear()
        {
            var h = new Harness(null, 3, true);
            h.Service.Context.BeginMatch("m-one", 1, 4, false, null);
            Phase(h);
            h.Service.Context.BeginMatch("m-two", 2, 5, true, new[] { "early_submit=carry" });
            Phase(h);
            h.Service.Context.EndMatch();
            Phase(h);
            var ev = h.Flushed();
            Assert.AreEqual("m-one", ev[0].MatchId); Assert.AreEqual(1, ev[0].WeekId);
            Assert.AreEqual("m-two", ev[1].MatchId); Assert.AreEqual(2, ev[1].WeekId); Assert.IsTrue(ev[1].HasBots);
            Assert.AreEqual("early_submit", ev[1].Experiments[0].Key);
            Assert.AreEqual("", ev[2].MatchId); Assert.AreEqual(-1, ev[2].WeekId);
            Assert.AreEqual(ev[0].SessionId, ev[2].SessionId, "session id outlives matches");
            var json = JsonDocument.Parse(TelemetryJson.ToLine(ev[2])).RootElement;
            Assert.IsFalse(json.TryGetProperty("match_id", out _));
            Assert.IsFalse(json.TryGetProperty("week_id", out _));
        }

        [Test]
        public void HostEventIds_AreDeterministicAndRetriesKeepThem()
        {
            string Id(string match, string key)
            {
                var h = new Harness(null, 3, true);
                h.Service.Context.BeginMatch(match, 1, 4, false, null);
                h.Service.Begin("week_started").Key(key).Int("days", 3).Int("tasks", 6).Emit();
                return h.Flushed()[0].EventId;
            }
            Assert.AreEqual(Id("m-a", "start"), Id("m-a", "start"));
            Assert.AreNotEqual(Id("m-a", "start"), Id("m-b", "start"));
            Assert.AreNotEqual(Id("m-a", "start"), Id("m-a", "other"));
            StringAssert.StartsWith("h-", Id("m-a", "start"));
            Assert.AreEqual("h-" + TelemetryNames.HostEventKey("m-a", "week_started", "start").ToString("x16"), Id("m-a", "start"));

            var hh = new Harness(null, 3, true);
            hh.Service.Context.BeginMatch("m-a", 1, 4, false, null);
            Assert.IsTrue(hh.Service.Begin("week_started").Key("start").Int("days", 3).Int("tasks", 6).Emit());
            Assert.IsFalse(hh.Service.Begin("week_started").Key("start").Int("days", 3).Int("tasks", 6).Emit(), "retry of the same logical event is dropped");
            Assert.AreEqual(1, hh.Flushed().Count);
            Assert.AreEqual(1, hh.Service.GetStats().Duplicates);
        }

        [Test]
        public void ClientEventIds_AreRandomAndFixedAtCreation()
        {
            var h = new Harness();
            h.Service.Begin("session_started").Enum("platform", "windows").Enum("language", "en").Bool("fullscreen", true).Bool("first_launch", false).Emit();
            h.Service.Begin("session_started").Enum("platform", "windows").Enum("language", "en").Bool("fullscreen", true).Bool("first_launch", false).Emit();
            var ev = h.Flushed();
            StringAssert.StartsWith("c-", ev[0].EventId);
            Assert.AreEqual(34, ev[0].EventId.Length);
            Assert.AreNotEqual(ev[0].EventId, ev[1].EventId);
        }

        // ---- free text / typing ------------------------------------------------------------------

        [Test]
        public void FreeText_CannotEnterAnEvent()
        {
            var h = new Harness();
            Assert.IsFalse(h.Service.Begin("phase_exited").Float("duration_seconds", 1).Enum("completion_reason", "I hate this game").Emit());
            Assert.IsFalse(h.Service.Begin("technical_failure").Enum("category", "ui_exception").Id("type", "some user typed text").Emit());
            Assert.IsFalse(h.Service.Begin("technical_failure").Enum("category", "ui_exception").Id("type", "Invalid\nName").Emit());
            Assert.IsFalse(h.Service.Begin("phase_exited").Float("duration_seconds", 1).Enum("completion_reason", "timer").Enum("no_such_param", "x").Emit());
            Assert.IsFalse(h.Service.Begin("phase_exited").Int("duration_seconds", 1).Enum("completion_reason", "timer").Emit(), "int into a float param");
            Assert.AreEqual(0, h.Flushed().Count);
            Assert.AreEqual(5, h.Service.GetStats().Rejected);
            Assert.IsTrue(h.Service.Begin("technical_failure").Enum("category", "ui_exception").Id("type", "NullReferenceException").Emit());
        }

        [Test]
        public void Builder_HasNoArbitraryStringApi()
        {
            var allowed = new HashSet<string> { "Enum", "Id", "Key" };
            foreach (var m in typeof(TelemetryEventBuilder).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var ps = m.GetParameters();
                for (int i = 1; i < ps.Length; i++)
                    if (ps[i].ParameterType == typeof(string)) Assert.IsTrue(allowed.Contains(m.Name), m.Name + " accepts a free string");
            }
            Assert.IsNull(typeof(TelemetryValue).GetMethod("OfStr", BindingFlags.Public | BindingFlags.Static), "string values are internal only");
            Assert.IsFalse(typeof(TelemetryEvent).GetFields().Any(f => f.Name == "Text" || f.Name == "Message"));
        }

        [Test]
        public void MissingData_IsOmittedNeverZero()
        {
            var h = new Harness(null, 3, true);
            h.Service.Context.BeginMatch("m-x", 1, 4, false, null);
            h.Service.Begin("phase_entered").Emit();                         // optional planned duration omitted
            h.Service.Begin("incident_created").Slot("actor", 1).Enum("kind", "invoice_padding").Slot("victim", -1).Emit();
            h.Service.SetParticipant(1, 3, "p-00000000000000a1", false);
            h.Service.Begin("incident_created").Slot("actor", 1).Enum("kind", "invoice_padding").Slot("victim", -1).Emit();
            h.Service.Begin("phase_exited").FloatIfKnown("duration_seconds", 3).Enum("completion_reason", "timer").FloatIfKnown("planned_duration_seconds", -1).Emit();
            Assert.IsFalse(h.Service.Begin("phase_exited").Enum("completion_reason", "timer").Emit(), "required duration missing is rejected, not zero-filled");
            h.Service.ReleasePrivate();
            var ev = h.Flushed();
            var pe = JsonDocument.Parse(TelemetryJson.ToLine(ev.First(e => e.Name == "phase_entered"))).RootElement.GetProperty("params");
            Assert.AreEqual(0, pe.EnumerateObject().Count());
            var px = JsonDocument.Parse(TelemetryJson.ToLine(ev.First(e => e.Name == "phase_exited"))).RootElement.GetProperty("params");
            Assert.IsFalse(px.TryGetProperty("planned_duration_seconds", out _));
            var inc = JsonDocument.Parse(TelemetryJson.ToLine(ev.First(e => e.Name == "incident_created"))).RootElement.GetProperty("params");
            Assert.IsFalse(inc.TryGetProperty("victim", out _));
            Assert.AreEqual(1, ev.Count(e => e.Name == "incident_created"), "first one was suppressed (slot unknown = no consent)");
        }

        // ---- consent -----------------------------------------------------------------------------

        [Test]
        public void NoCollection_BeforeConsent()
        {
            foreach (int level in new[] { 0, 1 })
            {
                var h = new Harness(null, level);
                Assert.IsFalse(h.Service.Begin("session_started").Enum("platform", "windows").Enum("language", "tr").Bool("fullscreen", false).Bool("first_launch", true).Emit());
                Assert.AreEqual(0, h.Flushed().Count);
                Assert.AreEqual(1, h.Service.GetStats().Suppressed);
            }
            var ok = new Harness(null, 2);
            Assert.IsTrue(ok.Service.Begin("session_started").Enum("platform", "windows").Enum("language", "tr").Bool("fullscreen", false).Bool("first_launch", true).Emit());
            Assert.AreEqual(1, ok.Flushed().Count);
        }

        [Test]
        public void HostConsent_DoesNotOverrideParticipant()
        {
            var h = new Harness(null, 3, true);
            h.Service.Context.BeginMatch("m-c", 1, 3, false, null);
            h.Service.SetParticipant(0, 3, "p-00000000000000a0", false);
            h.Service.SetParticipant(1, 1, "", false);                       // denied
            h.Service.SetParticipant(2, 2, "p-00000000000000a2", false);
            bool withDenied = h.Service.Begin("phone_call_requested").Slot("caller", 0).Slot("callee", 1).Bool("partners_in_task", true).Emit();
            bool reversed = h.Service.Begin("phone_call_requested").Slot("caller", 1).Slot("callee", 0).Bool("partners_in_task", true).Emit();
            bool both = h.Service.Begin("phone_call_requested").Slot("caller", 0).Slot("callee", 2).Bool("partners_in_task", true).Emit();
            bool self = h.Service.Begin("fault_points_changed").Slot("player", 1).Int("delta", 1).Enum("reason", "warning").Int("blame_after", 6).Emit();
            Assert.IsFalse(withDenied);
            Assert.IsFalse(reversed);
            Assert.IsTrue(both);
            Assert.IsFalse(self);
            h.Service.ReleasePrivate();
            var ev = h.Flushed();
            Assert.AreEqual(1, ev.Count);
            Assert.AreEqual(2, h.Service.GetStats().Suppressed - 1, "two phone calls + one blame suppressed (one extra counted for aggregate)");
        }

        [Test]
        public void Vote_TargetWithoutConsentIsWrittenAsUnconsented()
        {
            var h = new Harness(null, 3, true);
            h.Service.Context.BeginMatch("m-v", 1, 3, false, null);
            h.Service.SetParticipant(0, 3, "p-00000000000000a0", false);
            h.Service.SetParticipant(1, 1, "", false);
            h.Service.SetParticipant(2, 2, "p-00000000000000a2", false);
            h.Service.Begin("meeting_vote_submitted").Slot("voter", 0).Ballot("target", 1).Bool("changed", false).Emit();
            h.Service.Begin("meeting_vote_submitted").Slot("voter", 0).Ballot("target", 2).Bool("changed", false).Emit();
            h.Service.Begin("meeting_vote_submitted").Slot("voter", 0).Ballot("target", LineIds.Nobody).Bool("changed", false).Emit();
            h.Service.Begin("meeting_vote_submitted").Slot("voter", 0).Ballot("target", LineIds.None).Bool("changed", false).Emit();
            Assert.IsFalse(h.Service.Begin("meeting_vote_submitted").Slot("voter", 1).Ballot("target", 0).Bool("changed", false).Emit(), "a non-consenting voter is not logged");
            var t = h.Flushed().Select(e => e.GetString("target") ?? ("slot" + e.GetInt("target"))).ToArray();
            CollectionAssert.AreEqual(new[] { "unconsented", "slot2", "no_action", "abstain" }, t);
        }

        [Test]
        public void Withdrawal_PurgesQueueAndPrivateBuffer()
        {
            var h = new Harness(null, 3, true);
            h.Service.Context.BeginMatch("m-w", 1, 3, false, null);
            h.Service.SetParticipant(0, 3, "p-00000000000000a0", false);
            h.Service.SetParticipant(1, 3, "p-00000000000000a1", false);
            h.Service.Begin("fault_points_changed").Slot("player", 1).Int("delta", 1).Enum("reason", "warning").Int("blame_after", 6).Emit();
            h.Service.Begin("fault_points_changed").Slot("player", 0).Int("delta", 1).Enum("reason", "warning").Int("blame_after", 6).Emit();
            h.Service.Begin("choice_submitted").Slot("player", 1).Enum("field", "a").Int("category", 1).Float("latency_seconds", 4).Bool("after_contact", true).Enum("contact_channel", "voice").Bool("sabotage_option", false).Emit();
            h.Service.Begin("choice_submitted").Slot("player", 0).Enum("field", "a").Int("category", 1).Float("latency_seconds", 4).Bool("after_contact", true).Enum("contact_channel", "voice").Bool("sabotage_option", false).Emit();
            Assert.AreEqual(1, h.Service.GetStats().PrivateBuffered - 1);
            h.Service.SetParticipant(1, 1, "", false); // withdrawal
            var s = h.Service.GetStats();
            Assert.AreEqual(2, s.DroppedWithdrawal);
            Assert.AreEqual(1, s.QueueSize - 0, "only slot 0's blame event remains (plus nothing else)");
            h.Service.ReleasePrivate();
            var ev = h.Flushed();
            Assert.IsFalse(ev.Any(e => e.References(1)));
            Assert.AreEqual(2, ev.Count(e => e.References(0)));
            var report = ev.First(e => e.Name == "telemetry_queue_dropped");
            Assert.AreEqual("consent_withdrawn", report.GetString("reason"));
            Assert.AreEqual(2, report.GetInt("dropped"));
        }

        [Test]
        public void LocalWithdrawal_StopsCollectionImmediatelyAndPurges()
        {
            var h = new Harness(null, 3);
            h.Service.Begin("session_started").Enum("platform", "windows").Enum("language", "tr").Bool("fullscreen", false).Bool("first_launch", true).Emit();
            Assert.AreEqual(1, h.Service.GetStats().QueueSize);
            h.Service.SetLocalConsent(1);
            Assert.AreEqual(0, h.Service.GetStats().QueueSize);
            Assert.IsFalse(h.Service.Begin("session_ended").Float("duration_seconds", 5).Enum("reason", "quit").Emit());
            Assert.AreEqual(0, h.Flushed().Count);
        }

        [Test]
        public void Coverage_ReflectsConsentingHumans()
        {
            var h = new Harness(null, 3, true);
            h.Service.Context.BeginMatch("m-cov", 1, 4, false, null);
            h.Service.SetParticipant(0, 3, "p-00000000000000a0", false);
            h.Service.SetParticipant(1, 2, "p-00000000000000a1", false);
            h.Service.SetParticipant(2, 1, "", false);
            h.Service.SetParticipant(3, 0, "", true); // bot: not a human
            Phase(h);
            var e = h.Flushed()[0];
            Assert.AreEqual(2.0 / 3.0, e.Coverage, 1e-9);
            Assert.AreEqual("0.667", JsonDocument.Parse(TelemetryJson.ToLine(e)).RootElement.GetProperty("observation_coverage").GetRawText());
        }

        // ---- sensitivity -------------------------------------------------------------------------

        [Test]
        public void PrivateUntilMatchEnd_IsNotWrittenBeforeRelease()
        {
            var h = new Harness(null, 3, true);
            h.Service.Context.BeginMatch("m-p", 1, 3, false, null);
            h.Service.SetParticipant(0, 3, "p-00000000000000a0", false);
            h.Service.Begin("income_credited").Slot("player", 0).Int("amount", 2).Int("raw_amount", 2).Enum("reason", "task_income").Int("block_total", 2).Emit();
            Phase(h);
            h.Service.Flush();
            Assert.AreEqual(1, h.Sink.Events.Count, "only the public event was written");
            Assert.AreEqual(1, h.Service.GetStats().PrivateBuffered);
            h.Service.ReleasePrivate();
            h.Service.Flush();
            Assert.AreEqual(2, h.Sink.Events.Count);
            Assert.AreEqual("income_credited", h.Sink.Events[1].Name);
        }

        [Test]
        public void DebugSink_HidesPrivateDuringMatchAndHostEventsOnClients()
        {
            var dbg = new DebugTelemetrySink(16);
            var h = new Harness(null, 3, true);
            h.Service.AddSink(dbg);
            h.Service.Context.BeginMatch("m-d", 1, 3, false, null);
            h.Service.SetParticipant(0, 3, "p-00000000000000a0", false);
            Phase(h);
            h.Service.Begin("session_started").Enum("platform", "windows").Enum("language", "tr").Bool("fullscreen", false).Bool("first_launch", true).Emit();
            h.Service.Begin("income_credited").Slot("player", 0).Int("amount", 2).Int("raw_amount", 2).Enum("reason", "task_income").Int("block_total", 2).Emit();
            h.Service.ReleasePrivate();
            h.Service.Flush();
            Assert.AreEqual(3, dbg.Snapshot(true, false).Count);
            Assert.AreEqual(2, dbg.Snapshot(true, true).Count, "private events hidden while a match runs");
            Assert.AreEqual(1, dbg.Snapshot(false, false).Count, "non-hosts see client events only");
            Assert.IsTrue(dbg.Snapshot(false, false).All(e => e.Source == TelemetryAuthority.Client));
            for (int i = 0; i < 40; i++) Phase(h);
            h.Service.Flush();
            Assert.LessOrEqual(dbg.Snapshot(true, false, 1000).Count, dbg.Capacity, "ring is bounded");
        }

        // ---- queue bounds / failure isolation ----------------------------------------------------

        [Test]
        public void Overflow_DropsAndReports()
        {
            var h = new Harness(c => c.QueueBound = 16);
            for (int i = 0; i < 40; i++) Phase(h);
            var s = h.Service.GetStats();
            Assert.AreEqual(24, s.DroppedOverflow);
            Assert.AreEqual(16, s.QueueSize);
            var ev = h.Flushed();
            var drop = ev.Single(e => e.Name == "telemetry_queue_dropped");
            Assert.AreEqual(24, drop.GetInt("dropped"));
            Assert.AreEqual("overflow", drop.GetString("reason"));
            Assert.AreEqual(17, ev.Count, "16 kept + 1 report (the report is never dropped)");
        }

        [Test]
        public void Overflow_DropOldestKeepsNewest()
        {
            var h = new Harness(c => { c.QueueBound = 16; c.Overflow = OverflowPolicy.DropOldest; });
            for (int i = 0; i < 20; i++) { h.Clock = 100 + i; h.Service.Begin("phase_exited").Float("duration_seconds", i).Enum("completion_reason", "timer").Emit(); }
            var ev = h.Flushed().Where(e => e.Name == "phase_exited").ToList();
            Assert.AreEqual(4.0, ev.First().GetFloat("duration_seconds"));
            Assert.AreEqual(19.0, ev.Last().GetFloat("duration_seconds"));
        }

        [Test]
        public void ThrowingSink_NeverPropagatesAndIsReportedOnce()
        {
            var h = new Harness();
            var bad = new CaptureSink { Throw = true };
            h.Service.AddSink(bad);
            Assert.DoesNotThrow(() =>
            {
                Phase(h);
                h.Service.Flush();
                Phase(h);
                h.Service.Flush();
                Phase(h);
                h.Service.Flush();
            });
            Assert.AreEqual(3, h.Sink.Count("phase_exited"), "the healthy sink still gets everything");
            Assert.AreEqual(3, h.Service.GetStats().SinkErrors);
            var errs = h.Sink.Named("telemetry_sink_error").ToList();
            Assert.AreEqual(1, errs.Count, "one report per fault, not per retry");
            Assert.AreEqual("io", errs[0].GetString("category"));
            bad.Throw = false;
            Phase(h);
            h.Service.Flush();
            bad.Throw = true;
            Phase(h);
            h.Service.Flush();
            h.Service.Flush(); // the report itself is queued and travels with the next flush
            Assert.AreEqual(2, h.Sink.Named("telemetry_sink_error").Count(), "a new fault after recovery is reported again");
        }

        [Test]
        public void DisabledService_DoesNothingAndIsCheap()
        {
            var h = new Harness(c => c.Enabled = false);
            Assert.IsFalse(h.Service.IsEnabled);
            Assert.IsFalse(Phase(h));
            Assert.AreEqual(0, h.Service.GetStats().Emitted);
            Assert.AreEqual(0, h.Flushed().Count);
            Assert.AreEqual(0, h.Sink.WriteCalls);
            Assert.AreSame(h.Service.Begin("a"), h.Service.Begin("b"), "shared no-op builder");
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 1000000; i++) h.Service.Begin("phase_exited").Float("duration_seconds", i).Enum("completion_reason", "timer").Emit();
            sw.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine("disabled: 1M calls in " + sw.ElapsedMilliseconds + " ms, " + allocated + " bytes allocated");
            Assert.Less(allocated, 1024, "no per-call allocation when disabled (the delta is measurement noise)");
            Assert.Less(sw.ElapsedMilliseconds, 500);
        }

        [Test]
        public void EnabledOverhead_Measured()
        {
            var h = new Harness(c => c.QueueBound = 100000);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20000; i++) h.Service.Begin("phase_exited").Float("duration_seconds", i).Enum("completion_reason", "timer").Emit();
            sw.Stop();
            Console.WriteLine("enabled: 20k emits in " + sw.ElapsedMilliseconds + " ms (" + (sw.Elapsed.TotalMilliseconds * 1000 / 20000).ToString("0.0") + " us/event)");
            Assert.Less(sw.Elapsed.TotalMilliseconds * 1000 / 20000, 100, "under 100 us per event");
        }

        [Test]
        public void Routing_CloudNeedsConsent3AndConfig()
        {
            var h = new Harness(null, 3);
            h.Service.Begin("session_started").Enum("platform", "windows").Enum("language", "tr").Bool("fullscreen", false).Bool("first_launch", true).Emit();
            h.Service.Begin("lobby_joined").Bool("reconnect", false).Float("wait_seconds", 1).Emit();
            var cloud = new CaptureSink(TelemetrySinkKind.Cloud);
            h.Service.AddSink(cloud);
            h.Service.Begin("session_ended").Float("duration_seconds", 5).Enum("reason", "quit").Emit();
            h.Service.Flush();
            CollectionAssert.AreEqual(new[] { "session_started", "session_ended" }, cloud.Events.Select(e => e.Name).ToArray(), "only cloud-routed events reach the cloud sink (lobby_joined is local only)");
            Assert.AreEqual(3, h.Sink.Events.Count);

            var h2 = new Harness(null, 2);
            var cloud2 = new CaptureSink(TelemetrySinkKind.Cloud);
            h2.Service.AddSink(cloud2);
            h2.Service.Begin("session_ended").Float("duration_seconds", 5).Enum("reason", "quit").Emit();
            h2.Service.Flush();
            Assert.AreEqual(0, cloud2.Events.Count, "consent 2 = local only");
            Assert.AreEqual(1, h2.Sink.Events.Count);

            var h3 = new Harness(c => c.Cloud = false, 3);
            var cloud3 = new CaptureSink(TelemetrySinkKind.Cloud);
            h3.Service.AddSink(cloud3);
            h3.Service.Begin("session_ended").Float("duration_seconds", 5).Enum("reason", "quit").Emit();
            h3.Service.Flush();
            Assert.AreEqual(0, cloud3.Events.Count, "cloud disabled in configuration");
        }

        [Test]
        public void Json_IsValidAndHasEnvelope()
        {
            var h = new Harness(c => { c.Cohort = "friends_wave1"; c.Experiments.Add("rec_select=context"); c.Environment = "playtest"; });
            h.Service.Context.GameVersion = "0.4.0";
            h.Service.Context.LocalSlot = 2;
            h.Service.Context.LocalPlayerId = "p-00000000000000b2";
            h.Service.Begin("lobby_join_failed").Enum("reason", "reject.full").Emit();
            var line = TelemetryJson.ToLine(h.Flushed()[0]);
            var root = JsonDocument.Parse(line).RootElement;
            Assert.AreEqual(1, root.GetProperty("schema_version").GetInt32());
            Assert.AreEqual("lobby_join_failed", root.GetProperty("event").GetString());
            Assert.AreEqual("client", root.GetProperty("authority").GetString());
            Assert.AreEqual("playtest", root.GetProperty("environment").GetString());
            Assert.AreEqual("friends_wave1", root.GetProperty("test_cohort").GetString());
            Assert.AreEqual("context", root.GetProperty("experiments").GetProperty("rec_select").GetString());
            Assert.AreEqual(2, root.GetProperty("player_slot").GetInt32());
            Assert.AreEqual("p-00000000000000b2", root.GetProperty("anonymous_player_id").GetString());
            Assert.AreEqual("reject.full", root.GetProperty("params").GetProperty("reason").GetString());
            StringAssert.EndsWith("Z", root.GetProperty("timestamp_utc").GetString());
            Assert.IsFalse(line.Contains("\n"));
        }

        // ---- configuration -----------------------------------------------------------------------

        [Test]
        public void Configuration_LaunchArgs()
        {
            var c = new TelemetryConfiguration().ApplyLaunchArgs(new[] { "game.exe", "-telemetry", "off", "-cohort", "friends_wave1", "-playtestgroup", "G07", "-experiment", "rec_select=context", "-experiment", "BAD ONE" });
            Assert.IsFalse(c.Enabled);
            Assert.AreEqual("friends_wave1", c.Cohort);
            Assert.AreEqual("G07", c.GroupId);
            CollectionAssert.AreEqual(new[] { "rec_select=context" }, c.Experiments);
            Assert.IsTrue(new TelemetryConfiguration().ApplyLaunchArgs(new[] { "-telemetry", "cloud" }).Cloud);
            var local = new TelemetryConfiguration { Cloud = true }.ApplyLaunchArgs(new[] { "-telemetry", "local" });
            Assert.IsTrue(local.Local); Assert.IsFalse(local.Cloud);
            var bad = new TelemetryConfiguration { Cohort = "has spaces!", Environment = "weird", QueueBound = -5, FlushIntervalSeconds = float.NaN }.Normalize();
            Assert.AreEqual("", bad.Cohort); Assert.AreEqual("development", bad.Environment); Assert.GreaterOrEqual(bad.QueueBound, 16); Assert.AreEqual(2f, bad.FlushIntervalSeconds);
        }

        // ---- catalog -----------------------------------------------------------------------------

        [Test]
        public void Catalog_Invariants()
        {
            var seen = new HashSet<string>();
            foreach (var d in TelemetryCatalog.All)
            {
                Assert.IsTrue(seen.Add(d.Name), d.Name);
                StringAssert.IsMatch("^[a-z][a-z0-9_]*$", d.Name);
                Assert.IsTrue(d.Params.Select(p => p.Name).Distinct().Count() == d.Params.Length, d.Name + " duplicate param");
                foreach (var p in d.Params)
                {
                    StringAssert.IsMatch("^[a-z][a-z0-9_]*$", p.Name);
                    Assert.IsFalse(new[] { "text", "message", "name", "body", "transcript", "email", "ip", "username", "address" }.Contains(p.Name), d.Name + "." + p.Name + " looks like free text");
                    if (p.Type == ParamType.Enum || p.Type == ParamType.SlotOrEnum)
                    {
                        Assert.Greater(p.EnumValues.Length, 0, d.Name + "." + p.Name);
                        foreach (var v in p.EnumValues) StringAssert.IsMatch("^[a-z0-9_.]+$", v);
                    }
                    if (p.Cloud) Assert.IsTrue((d.Routing & TelemetryRouting.Cloud) != 0, d.Name + " cloud param on a local-only event");
                }
                if ((d.Routing & TelemetryRouting.Cloud) != 0)
                {
                    bool clientSelf = d.Authority == TelemetryAuthority.Client && d.Consent == ConsentRule.Self;
                    bool hostAgg = d.Consent == ConsentRule.HostAggregate && !d.Params.Any(p => p.Type == ParamType.Slot || p.Type == ParamType.SlotOrEnum);
                    bool both = d.Authority == TelemetryAuthority.Both && d.Consent == ConsentRule.Self;
                    Assert.IsTrue(clientSelf || hostAgg || both, d.Name + " routes other players' data to the cloud");
                    Assert.IsTrue(d.Sensitivity == TelemetrySensitivity.Public, d.Name + " cloud + private");
                }
                if (d.Consent == ConsentRule.HostAggregate)
                    Assert.IsFalse(d.Params.Any(p => p.Type == ParamType.Slot || p.Type == ParamType.SlotOrEnum || p.Id == IdKind.PlayerId), d.Name + " aggregate with per-player ids");
                if (d.Authority == TelemetryAuthority.Client) Assert.IsFalse(d.Params.Any(p => p.Type == ParamType.Slot), d.Name + " client event with slot params");
                if (d.Availability == TelemetryAvailability.Implemented) Assert.IsNotEmpty(d.Trigger, d.Name);
                else Assert.IsNotEmpty(d.AvailabilityNote, d.Name + " needs the exact missing source");
                if (d.Sensitivity == TelemetrySensitivity.PrivateUntilMatchEnd) Assert.AreEqual(TelemetryAuthority.Host, d.Authority, d.Name);
            }
            foreach (var n in new[] { "session_started", "phase_exited", "phone_round_summary", "round_player_summary", "meeting_vote_resolved", "final_vote_resolved", "career_breakdown", "company_result", "technical_failure", "telemetry_queue_dropped", "telemetry_sink_error", "player_slot_map" })
                Assert.IsNotNull(TelemetryCatalog.Find(n), n);
            Assert.AreEqual(TelemetryAvailability.NotApplicable, TelemetryCatalog.Get("final_candidate_selected").Availability);
            Assert.IsNull(TelemetryCatalog.Find("lie_detected"));
            Assert.IsNull(TelemetryCatalog.Find("lie_detected_by_ai"));
        }

        [Test]
        public void UnsupportedAndNotApplicable_CannotBeEmitted()
        {
            var h = new Harness(null, 3, true);
            Assert.IsFalse(h.Service.Begin("final_candidate_selected").Emit());
            Assert.IsFalse(h.Service.Begin("unknown_event").Emit());
            Assert.AreEqual(2, h.Service.GetStats().Rejected);
        }

        [Test]
        public void EnumNaming_Snake()
        {
            Assert.AreEqual("hung_up", TelemetryNames.Of(CallEndReason.HungUp));
            Assert.AreEqual("it_log", TelemetryNames.Snake("ITLog"));
            Assert.AreEqual("meeting_dossier", TelemetryNames.Of(LinePhase.MeetingDossier));
            Assert.AreEqual("invoice_reconcile", LineTasks.Id(TaskKind.InvoiceReconcile));
        }
    }
}
