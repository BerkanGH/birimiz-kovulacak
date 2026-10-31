using System;
using System.Collections.Generic;
using System.Linq;
using BirimizKovulacak.Core;
using NUnit.Framework;

namespace BirimizKovulacak.Telemetry.Tests
{
    /// <summary>Regressions from the independent review of the integrated v4 branch (H2-H5, M9-M14).</summary>
    public class RegressionTests
    {
        static MatchRig InRound(int[] consents = null)
        {
            var rig = new MatchRig(4, 3, null, consents);
            rig.Engine.Groups[0] = new List<int> { 1 }; rig.Engine.Groups[1] = new List<int> { 0 };
            rig.Engine.Groups[2] = new List<int> { 3 }; rig.Engine.Groups[3] = new List<int> { 2 };
            rig.Go(1000, LinePhase.Morning, 12, PhaseEndReason.Start, -1, -1, 0);
            rig.Go(1012, LinePhase.RoundIntro, 12, PhaseEndReason.Timer, 0, 0, 0);
            rig.Go(1024, LinePhase.RoundPlay, 75);
            return rig;
        }

        // ---- H2 ----------------------------------------------------------------------------------

        [Test]
        public void H2_PrivateActionsAreNotWrittenWhileTheMatchRuns()
        {
            var rig = InRound();
            rig.Feed(Ev.Incoming(1030, 0, 1));
            rig.Feed(Ev.Connected(1032, 0, 1, 7, 0), Ev.Connected(1032, 0, 1, 7, 1));
            rig.Feed(Ev.Ended(1050, 0, 1, CallEndReason.HungUp, 7));
            rig.Feed(Ev.Incoming(1052, 2, 3));
            rig.Feed(Ev.Ended(1053, 3, 2, CallEndReason.Declined, -1));
            rig.Feed(new LineEvent { Type = LineEventType.RemedialRequested, Time = 1060, Actor = 1, Audience = LineAudience.Player, AudiencePlayer = 2, A = 0 });
            rig.Feed(Ev.Pub(LineEventType.RemedialResolved, 1065, actor: 1, target: 2, a: 1, b: 0));
            rig.Go(1099, LinePhase.RoundReveal, 10);
            var written = rig.Events;
            foreach (var e in written)
                Assert.IsFalse(e.Name.StartsWith("phone_call_") || e.Name == "phone_round_summary" || e.Name.StartsWith("remedial_"), e.Name + " is private until the match ends");
            rig.H.Service.ReleasePrivate();
            var after = rig.H.Flushed();
            Assert.AreEqual(1, after.Count(e => e.Name == "phone_call_connected"));
            Assert.AreEqual(1, after.Count(e => e.Name == "remedial_resolved"));
        }

        [Test]
        public void H2_OnlyFactsEveryoneCanSeeAreWrittenImmediately()
        {
            // A public host event that carries a player slot must be something all participants already see.
            var allowed = new HashSet<string>
            {
                "player_ready_changed", "player_disconnected", "player_reconnected", "evidence_presented", "meeting_vote_submitted",
                "final_vote_submitted", "warning_assigned", "fault_points_changed", "defense_started", "defense_finished", "player_fired",
                "sabotage_verified", "career_breakdown", "recording_playback_started", "player_slot_map",
            };
            foreach (var d in TelemetryCatalog.All)
            {
                if (d.Authority != TelemetryAuthority.Host || d.Sensitivity != TelemetrySensitivity.Public) continue;
                if (!d.Params.Any(p => p.Type == ParamType.Slot || p.Type == ParamType.SlotOrEnum)) continue;
                Assert.IsTrue(allowed.Contains(d.Name), d.Name + " carries a slot and is public: confirm the game shows it to everyone or buffer it");
            }
            foreach (var n in new[] { "phone_call_requested", "phone_call_connected", "phone_call_rejected", "phone_call_missed", "phone_call_failed", "phone_call_finished", "phone_round_summary", "remedial_requested", "remedial_resolved", "objective_completed" })
                Assert.AreEqual(TelemetrySensitivity.PrivateUntilMatchEnd, TelemetryCatalog.Get(n).Sensitivity, n);
        }

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
            h.Service.Begin("fault_points_changed").Slot("player", 1).Int("delta", 1).Enum("reason", "warning").Int("blame_after", 6).Emit();
            h.Service.Flush();
            Assert.AreEqual(1, a.Events.Count);
            Assert.AreEqual(0, b.Events.Count);
        }

        // ---- H4 ----------------------------------------------------------------------------------

        [Test]
        public void H4_ConsentGrantedMidRoundDoesNotReportEarlierActivity()
        {
            var rig = InRound(new[] { 1, 1, 3, 3 });
            rig.Feed(Ev.Incoming(1030, 0, 1));
            rig.Feed(Ev.Connected(1030, 0, 1, 7, 0), Ev.Connected(1030, 0, 1, 7, 1));
            rig.Feed(Ev.Ended(1040, 0, 1, CallEndReason.HungUp, 7), Ev.Ended(1040, 1, 0, CallEndReason.HungUp, 7));
            rig.Host.PlayerConsent(0, 3, "p-00000000000000a0", false);
            rig.Host.PlayerConsent(1, 3, "p-00000000000000a1", false);
            rig.Go(1099, LinePhase.RoundReveal, 10);
            rig.Host.MatchEnded(rig.Engine, "host_closed", 1100);
            var ev = rig.Events;
            Assert.AreEqual(0, ev.Count(e => e.Name == "round_player_summary" && (e.GetInt("player") == 0 || e.GetInt("player") == 1)), "partially observed: no per-player summary");
            Assert.AreEqual(0, ev.Count(e => e.Name == "phone_call_finished"), "its start was never observed");
            var s = ev.Single(e => e.Name == "phone_round_summary");
            Assert.AreEqual(0, s.GetInt("connected"));
            Assert.AreEqual(0.0, s.GetFloat("connected_seconds_union"), 1e-9);
            Assert.AreEqual(0, s.GetInt("participating"));
            Assert.AreEqual(2, s.GetInt("observed"), "only the two players consenting for the whole window");
        }

        // ---- H5 ----------------------------------------------------------------------------------

        [Test]
        public void H5_ObjectiveResultsStaySecretUntilWeekResults()
        {
            var rig = InRound();
            rig.Go(1099, LinePhase.RoundReveal, 10); rig.Go(1109, LinePhase.Break, 15); rig.Go(1124, LinePhase.MeetingDossier, 24);
            rig.Engine.Meeting = new LineMeetingView { IsFinal = true, Roster = new List<int> { 0, 1, 2, 3 } };
            rig.Go(1148, LinePhase.MeetingDiscussion, 75);
            rig.Go(1223, LinePhase.FinalDefense, 60);
            rig.Go(1283, LinePhase.FinalVote, 25);
            rig.Go(1308, LinePhase.FinalVerdict, 10);
            for (int i = 0; i < 4; i++) rig.Feed(Ev.Host(LineEventType.HostObjective, 1308, actor: i, target: -1, a: (int)ObjectiveKind.Saver, b: 1));
            rig.H.Service.Flush();
            Assert.AreEqual(0, rig.H.Sink.Count("objective_completed"), "nothing is written at FinalVerdict");
            Assert.AreEqual(TelemetrySensitivity.PrivateUntilMatchEnd, TelemetryCatalog.Get("objective_completed").Sensitivity);
            rig.Engine.Result = new LineWeekResult { WeekId = 1, MatchId = "m-test0001", Fired = LineIds.Nobody, FinalProductivityPermille = 700, TargetPercent = 65 };
            for (int i = 0; i < 4; i++) rig.Engine.Result.Players.Add(new LineWeekPlayerResult { Player = i, Objective = ObjectiveKind.Saver, ObjectiveDone = true });
            Assert.AreEqual(0, rig.H.Sink.Count("objective_completed"));
            rig.Go(1318, LinePhase.WeekResults, 45);
            rig.H.Service.Flush();
            Assert.AreEqual(4, rig.H.Sink.Count("objective_completed"), "one per player after results, not duplicated by the early HostObjective event");
        }

        // ---- M9 ----------------------------------------------------------------------------------

        [Test]
        public void M9_RepeatedIdenticalIncomesAreAllRecorded()
        {
            var rig = InRound();
            for (int i = 0; i < 8; i++) rig.Feed(Ev.Host(LineEventType.HostIncome, 1100, actor: 1, a: 1, b: (int)MoneyReason.TaskIncome, c: 1));
            rig.Host.MatchEnded(rig.Engine, "host_closed", 1200);
            var inc = rig.Named("income_credited").ToList();
            Assert.AreEqual(8, inc.Count);
            Assert.AreEqual(8, inc.Select(e => e.EventId).Distinct().Count());
            Assert.AreEqual(8, inc.Max(e => e.GetInt("block_total")));
        }

        // ---- M10 ---------------------------------------------------------------------------------

        [Test]
        public void M10_RecordingsOfASecondMeetingAreNotDroppedAsDuplicates()
        {
            var rig = InRound();
            rig.Feed(Ev.Connected(1030, 0, 1, 7, 0)); rig.Feed(Ev.Ended(1050, 0, 1, CallEndReason.HungUp, 7));
            var info = new TelemetryRecordingInfo { Candidates = 1, ChosenRank = 0, SpeechMs = 3000, SnippetMs = 6000, CallLengthMs = 20000, Failure = "none" };
            rig.Go(1099, LinePhase.RoundReveal, 10); rig.Go(1109, LinePhase.Break, 15); rig.Go(1124, LinePhase.MeetingDossier, 24);
            rig.Host.RecordingStarted(rig.Engine, new LineRecordingPick { Index = 0, Caller = 0, Callee = 1, Context = TaskKind.InvoiceReconcile }, 7, info, 1130);
            rig.Host.RecordingFinished(7, 6, false, 1137);
            rig.Go(1148, LinePhase.MeetingDiscussion, 50); rig.Go(1198, LinePhase.MeetingVote, 20); rig.Go(1218, LinePhase.MeetingVerdict, 6);
            rig.Go(1224, LinePhase.Morning, 12, PhaseEndReason.Timer, -1, -1, 1);
            rig.Go(1236, LinePhase.RoundIntro, 12, PhaseEndReason.Timer, 1, 1, 1);
            rig.Go(1248, LinePhase.RoundPlay, 75);
            rig.Feed(Ev.Connected(1250, 0, 1, 8, 0)); rig.Feed(Ev.Ended(1270, 0, 1, CallEndReason.HungUp, 8));
            rig.Go(1323, LinePhase.RoundReveal, 10); rig.Go(1333, LinePhase.Break, 15); rig.Go(1348, LinePhase.MeetingDossier, 24);
            rig.Host.RecordingStarted(rig.Engine, new LineRecordingPick { Index = 0, Caller = 0, Callee = 1, Context = TaskKind.ServerCode }, 8, info, 1355);
            rig.Host.RecordingFinished(8, 6, false, 1362);
            rig.H.Service.Flush();
            Assert.AreEqual(2, rig.H.Sink.Count("recording_playback_started"));
            Assert.AreEqual(2, rig.H.Sink.Count("recording_playback_finished"));
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

        // ---- M12 ---------------------------------------------------------------------------------

        [Test]
        public void M12_TaskOutcomeDetailJoinsInEitherOrder()
        {
            var rig = InRound();
            rig.Engine.Reveals.Add(new LineTaskReveal { Number = 0, Output = 70, MaxOutput = 100 });
            rig.Go(1099, LinePhase.RoundReveal, 10);
            rig.Feed(Ev.Host(LineEventType.HostTaskOutcome, 1099, a: 0, b: 30, c: 21), Ev.Pub(LineEventType.TaskResolved, 1099, a: 0, b: 70));
            var to = rig.Named("task_output_resolved").Single();
            Assert.AreEqual(30, to.GetInt("sabotage_loss"));
            Assert.AreEqual(2, to.GetInt("groups")); Assert.AreEqual(1, to.GetInt("groups_full"));
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
