using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BirimizKovulacak.Core;
using NUnit.Framework;

namespace BirimizKovulacak.Telemetry.Tests
{
    public class ObserverTests
    {
        const double R0 = 1024; // RoundPlay start in the scripted match

        static MatchRig NewRig(int players = 4, int[] consents = null)
        {
            var rig = new MatchRig(players, 3, null, consents);
            rig.Engine.Groups[0] = new List<int> { 1 }; rig.Engine.Groups[1] = new List<int> { 0 };
            rig.Engine.Groups[2] = new List<int> { 3 }; rig.Engine.Groups[3] = new List<int> { 2 };
            rig.Go(1000, LinePhase.Morning, 12, PhaseEndReason.Start, -1, -1, 0);
            rig.Go(1012, LinePhase.RoundIntro, 12, PhaseEndReason.Timer, 0, 0, 0);
            return rig;
        }

        static MatchRig InRound(int players = 4, int[] consents = null)
        {
            var rig = NewRig(players, consents);
            rig.Go(R0, LinePhase.RoundPlay, 75);
            return rig;
        }

        static TelemetryEvent One(MatchRig r, string name) { return r.Events.Single(e => e.Name == name); }

        // ---- phases ------------------------------------------------------------------------------

        [Test]
        public void Phases_RecordPlannedVsActualAndCompletionReason()
        {
            var rig = InRound();
            rig.Go(1090, LinePhase.RoundReveal, 10, PhaseEndReason.TimerShortened);
            rig.Go(1105, LinePhase.Break, 15, PhaseEndReason.Timer);
            var ex = rig.Named("phase_exited").ToList();
            var morning = ex[0];
            Assert.AreEqual("morning", morning.Phase);
            Assert.AreEqual(12.0, morning.GetFloat("duration_seconds"), 1e-6);
            Assert.AreEqual(12.0, morning.GetFloat("planned_duration_seconds"), 1e-6);
            Assert.AreEqual("timer", morning.GetString("completion_reason"));
            Assert.IsFalse(morning.TryGet("overrun_seconds", out _));
            var play = ex.Single(e => e.Phase == "round_play");
            Assert.AreEqual(66.0, play.GetFloat("duration_seconds"), 1e-6);
            Assert.AreEqual(75.0, play.GetFloat("planned_duration_seconds"), 1e-6, "planned stays the engine's value at entry");
            Assert.AreEqual("timer_shortened", play.GetString("completion_reason"));
            var entered = rig.Named("phase_entered").Single(e => e.Phase == "round_play");
            Assert.AreEqual(75.0, entered.GetFloat("planned_duration_seconds"), 1e-6);
            Assert.AreEqual(0, entered.DayIndex);
            Assert.AreEqual("invoice_reconcile", entered.TaskId);
            Assert.AreEqual(0, entered.TaskNumber);
        }

        [Test]
        public void Phases_OverrunAndHostContinue()
        {
            var rig = NewRig();
            rig.Go(1030, LinePhase.RoundPlay, 75); // intro planned 12, took 18
            rig.Go(1100, LinePhase.RoundReveal, 10, PhaseEndReason.HostContinue);
            var intro = rig.Named("phase_exited").Single(e => e.Phase == "round_intro");
            Assert.AreEqual(6.0, intro.GetFloat("overrun_seconds"), 1e-6);
            Assert.AreEqual("host_continue", rig.Named("phase_exited").Single(e => e.Phase == "round_play").GetString("completion_reason"));
        }

        [Test]
        public void Phases_UnexpectedTransitionAndStuckPhase()
        {
            var rig = NewRig();
            rig.Go(1020, LinePhase.MeetingVote, 20); // RoundIntro -> MeetingVote is not in the documented flow
            Assert.AreEqual(1, rig.Named("unexpected_transition").Count());
            var u = One(rig, "unexpected_transition");
            Assert.AreEqual("round_intro", u.GetString("from")); Assert.AreEqual("meeting_vote", u.GetString("to"));
            rig.Host.EngineTick(rig.Engine, 1030);
            Assert.AreEqual(0, rig.Named("stuck_phase").Count());
            rig.Host.EngineTick(rig.Engine, 1020 + 20 + 6);
            rig.Host.EngineTick(rig.Engine, 1020 + 20 + 9);
            var s = rig.Named("stuck_phase").Single();
            Assert.AreEqual("meeting_vote", s.GetString("phase"));
            Assert.AreEqual(6.0, s.GetFloat("seconds_over"), 1e-6);
        }

        [Test]
        public void Week_StartEmitsPlannedStructure()
        {
            var rig = NewRig();
            var w = One(rig, "week_started");
            Assert.AreEqual(3, w.GetInt("days"));
            Assert.AreEqual(6, w.GetInt("tasks"));
            Assert.AreEqual("m-test0001", w.MatchId);
            Assert.AreEqual(4, rig.Named("player_slot_map").Count());
            Assert.AreEqual("G07", w.GroupId);
            Assert.AreEqual("s-lobby0001", w.SessionId);
            Assert.AreEqual("context", w.Experiments.Single(x => x.Key == "rec_select").Value);
            var ds = One(rig, "day_started");
            Assert.AreEqual("invoice_reconcile", ds.GetString("task_a")); Assert.AreEqual("server_code", ds.GetString("task_b"));
        }

        // ---- phone -------------------------------------------------------------------------------

        static void ScriptedCalls(MatchRig rig)
        {
            rig.Feed(Ev.Incoming(1030, 0, 1));
            rig.Feed(Ev.Connected(1035, 0, 1, 7, 0), Ev.Connected(1035, 0, 1, 7, 1));
            rig.Feed(Ev.Ended(1055, 0, 1, CallEndReason.HungUp, 7), Ev.Ended(1055, 1, 0, CallEndReason.HungUp, 7));
            rig.Feed(Ev.Incoming(1060, 2, 3));
            rig.Feed(Ev.Ended(1062, 3, 2, CallEndReason.Declined, -1), Ev.Ended(1062, 2, 3, CallEndReason.Declined, -1));
            rig.Feed(Ev.Incoming(1065, 3, 2));
            rig.Feed(Ev.Ended(1080, 2, 3, CallEndReason.Missed, -1), Ev.Ended(1080, 3, 2, CallEndReason.Missed, -1));
            rig.Feed(Ev.Busy(1085, 2, 0), new LineEvent { Type = LineEventType.MissedCall, Time = 1085, Actor = 2, Audience = LineAudience.Player, AudiencePlayer = 0 });
        }

        [Test]
        public void Phone_CallLifecycleEvents_AreEmittedOncePerCall()
        {
            var rig = InRound();
            ScriptedCalls(rig);
            Assert.AreEqual(4, rig.Named("phone_call_requested").Count());
            var c = One(rig, "phone_call_connected");
            Assert.AreEqual(7, c.GetInt("call_id")); Assert.AreEqual(5.0, c.GetFloat("ring_seconds"), 1e-6); Assert.IsTrue(c.GetBool("partners_in_task"));
            Assert.AreEqual(0, c.GetInt("caller")); Assert.AreEqual(1, c.GetInt("callee"));
            var f = One(rig, "phone_call_finished");
            Assert.AreEqual(20.0, f.GetFloat("duration_seconds"), 1e-6); Assert.AreEqual("hung_up", f.GetString("end_reason")); Assert.IsFalse(f.GetBool("censored"));
            Assert.AreEqual(1, rig.Named("phone_call_rejected").Count());
            Assert.AreEqual(1, rig.Named("phone_call_missed").Count());
            var failed = One(rig, "phone_call_failed");
            Assert.AreEqual("busy", failed.GetString("reason"));
            Assert.AreEqual(2, failed.GetInt("caller")); Assert.AreEqual(0, failed.GetInt("callee"));
        }

        [Test]
        public void Phone_FailedRejectedMissedNeverCountAsParticipation()
        {
            var rig = InRound();
            ScriptedCalls(rig);
            rig.Go(1099, LinePhase.RoundReveal, 10);
            rig.Host.MatchEnded(rig.Engine, "host_closed", 1100);
            var s = One(rig, "phone_round_summary");
            Assert.AreEqual(4, s.GetInt("eligible")); Assert.AreEqual(4, s.GetInt("observed"));
            Assert.AreEqual(2, s.GetInt("participating"), "only players 0 and 1 had a successful connected call");
            Assert.AreEqual(0.5, s.GetFloat("participation_rate"), 1e-9);
            Assert.AreEqual(4, s.GetInt("attempts"));
            Assert.AreEqual(1, s.GetInt("connected"));
            Assert.AreEqual(1, s.GetInt("distinct_pairs"));
            Assert.AreEqual(20.0, s.GetFloat("connected_seconds_union"), 1e-6);
            var pps = rig.Named("round_player_summary").ToDictionary(e => (int)e.GetInt("player"));
            Assert.AreEqual(20.0, pps[0].GetFloat("connected_seconds"), 1e-6);
            Assert.AreEqual("voice", pps[0].GetString("comm_mode"));
            Assert.AreEqual(1, pps[0].GetInt("unique_partners"));
            Assert.AreEqual(11.0, pps[0].GetFloat("first_contact_latency_seconds"), 1e-6, "first connected call at 1035 - round start 1024");
            Assert.AreEqual("none", pps[2].GetString("comm_mode"));
            Assert.AreEqual(0, pps[2].GetInt("calls_connected"));
            Assert.AreEqual(1, pps[2].GetInt("busy_failures"));
            Assert.AreEqual(1, pps[2].GetInt("unanswered_attempts"));
            Assert.AreEqual(2, pps[2].GetInt("calls_attempted"));
            Assert.IsFalse(pps[2].TryGet("first_contact_latency_seconds", out _), "no contact: omitted, never 0");
            Assert.AreEqual(1, pps[3].GetInt("unanswered_attempts"));
        }

        [Test]
        public void Phone_NoCallRound_OmitsLatencyAndReportsZeroParticipation()
        {
            var rig = InRound();
            rig.Go(1099, LinePhase.RoundReveal, 10);
            rig.Host.MatchEnded(rig.Engine, "host_closed", 1100);
            var s = One(rig, "phone_round_summary");
            Assert.AreEqual(0, s.GetInt("participating"));
            Assert.AreEqual(0.0, s.GetFloat("participation_rate"), 1e-9);
            foreach (var p in rig.Named("round_player_summary"))
            {
                Assert.IsFalse(p.TryGet("first_contact_latency_seconds", out _));
                Assert.IsFalse(p.TryGet("post_submit_wait_seconds", out _));
                Assert.AreEqual(75.0, p.GetFloat("longest_gap_seconds"), 1e-6, "the whole task window passed without an observable interaction");
            }
        }

        [Test]
        public void Phone_ParticipationRateOmittedWhenNobodyObserved()
        {
            var rig = InRound(3, new[] { 1, 0, 1 });
            rig.Go(1099, LinePhase.RoundReveal, 10);
            var s = One(rig, "phone_round_summary");
            Assert.AreEqual(0, s.GetInt("observed"));
            Assert.IsFalse(s.TryGet("participation_rate", out _), "not_measured");
            Assert.AreEqual(0, rig.Named("round_player_summary").Count());
        }

        [Test]
        public void Phone_ConsentGatesCallsWithAnUnconsentingParticipant()
        {
            var rig = InRound(4, new[] { 3, 3, 1, 3 });
            rig.Feed(Ev.Incoming(1030, 0, 2));
            rig.Feed(Ev.Connected(1032, 0, 2, 9, 0), Ev.Connected(1032, 0, 2, 9, 2));
            rig.Feed(Ev.Ended(1050, 0, 2, CallEndReason.HungUp, 9));
            rig.Feed(Ev.Incoming(1052, 0, 1));
            rig.Feed(Ev.Connected(1054, 0, 1, 10, 0));
            rig.Feed(Ev.Ended(1070, 0, 1, CallEndReason.HungUp, 10));
            rig.Go(1099, LinePhase.RoundReveal, 10);
            var ev = rig.Events;
            Assert.AreEqual(1, ev.Count(e => e.Name == "phone_call_connected"), "the call with the non-consenting colleague is suppressed");
            Assert.AreEqual(10, ev.Single(e => e.Name == "phone_call_connected").GetInt("call_id"));
            Assert.AreEqual(1, ev.Count(e => e.Name == "phone_call_finished"));
            Assert.AreEqual(1, ev.Count(e => e.Name == "phone_call_requested"));
            var s = ev.Single(e => e.Name == "phone_round_summary");
            Assert.AreEqual(3, s.GetInt("observed"));
            Assert.AreEqual(2, s.GetInt("participating"), "players 0 and 1 (self-data of consenting players)");
            Assert.AreEqual(0.667, s.GetFloat("participation_rate"), 0.001);
            Assert.IsFalse(ev.Any(e => e.Name == "round_player_summary" && e.GetInt("player") == 2));
            Assert.AreEqual(0.75, s.Coverage, 1e-9);
        }

        [Test]
        public void Phone_DisconnectCensorsOpenCallsOnce()
        {
            var rig = InRound();
            rig.Feed(Ev.Incoming(1030, 0, 1));
            rig.Feed(Ev.Connected(1032, 0, 1, 5, 0), Ev.Connected(1032, 0, 1, 5, 1));
            rig.Engine.Disconnected[1] = true;
            rig.Host.PlayerConnection(1, false, 1040);
            rig.Feed(new LineEvent { Type = LineEventType.Presence, Time = 1040, Target = 1, A = 0 });  // same fact from the engine: no second report
            rig.Feed(Ev.Ended(1040.5, 0, 1, CallEndReason.Disconnected, 5), Ev.Ended(1040.5, 1, 0, CallEndReason.Disconnected, 5));
            var fin = One(rig, "phone_call_finished");
            Assert.IsTrue(fin.GetBool("censored"));
            Assert.AreEqual(8.0, fin.GetFloat("duration_seconds"), 1e-6, "lower bound at the disconnect, not the engine's later end");
            Assert.AreEqual("disconnected", fin.GetString("end_reason"));
            Assert.AreEqual(1, rig.Named("player_disconnected").Count());
            rig.Engine.Disconnected[1] = false;
            rig.Host.PlayerConnection(1, true, 1060);
            var back = One(rig, "player_reconnected");
            Assert.AreEqual(20.0, back.GetFloat("downtime_seconds"), 1e-6);
            Assert.AreEqual("round_play", back.GetString("phase"));
        }

        [Test]
        public void Phone_OpenCallsAreCensoredAtMatchEnd()
        {
            var rig = InRound();
            rig.Feed(Ev.Connected(1032, 0, 1, 5, 0));
            rig.Host.MatchEnded(rig.Engine, "host_closed", 1050);
            var fin = One(rig, "phone_call_finished");
            Assert.IsTrue(fin.GetBool("censored"));
            Assert.AreEqual(18.0, fin.GetFloat("duration_seconds"), 1e-6);
        }

        [Test]
        public void Phone_SummaryClipsCallsToTheTaskWindow()
        {
            var rig = NewRig();
            rig.Feed(Ev.Connected(1015, 0, 1, 3, 0)); // connects during the intro
            rig.Go(R0, LinePhase.RoundPlay, 75);
            rig.Feed(Ev.Ended(1030, 0, 1, CallEndReason.HungUp, 3));
            rig.Feed(Ev.Connected(1090, 0, 2, 4, 0)); // runs past the end of the window
            rig.Go(1099, LinePhase.RoundReveal, 10);
            rig.Feed(Ev.Ended(1120, 0, 2, CallEndReason.PhaseEnd, 4));
            rig.Host.MatchEnded(rig.Engine, "host_closed", 1130);
            var pps = rig.Named("round_player_summary").ToDictionary(e => (int)e.GetInt("player"));
            Assert.AreEqual(6.0 + 9.0, pps[0].GetFloat("connected_seconds"), 1e-6);
            Assert.AreEqual(2, pps[0].GetInt("calls_connected"));
            Assert.AreEqual(2, pps[0].GetInt("unique_partners"));
            Assert.AreEqual(6.0, pps[1].GetFloat("connected_seconds"), 1e-6);
            // the finished events keep the real (unclipped) call length
            Assert.AreEqual(30.0, rig.Named("phone_call_finished").Single(e => e.GetInt("call_id") == 4).GetFloat("duration_seconds"), 1e-6);
        }

        [Test]
        public void Phone_RetriesOfTheSameEngineEventsAreIdempotent()
        {
            var rig = InRound();
            ScriptedCalls(rig);
            ScriptedCalls(rig);
            Assert.AreEqual(1, rig.Named("phone_call_connected").Count());
            Assert.AreEqual(1, rig.Named("phone_call_finished").Count());
            Assert.AreEqual(1, rig.Named("phone_call_rejected").Count());
            Assert.AreEqual(4, rig.Named("phone_call_requested").Count());
        }

        // ---- choices -----------------------------------------------------------------------------

        [Test]
        public void Choices_LatencyAndContactAreDerivedFromCallsAndMessages()
        {
            var rig = InRound();
            rig.Feed(Ev.Incoming(1030, 0, 1));
            rig.Feed(Ev.Connected(1030, 0, 1, 7, 0), Ev.Connected(1030, 0, 1, 7, 1));
            rig.Feed(Ev.Host(LineEventType.HostChoice, 1040, actor: 0, target: (int)TaskField.A, a: 1, b: 7, c: 0));
            rig.Feed(Ev.Host(LineEventType.HostChoice, 1044, actor: 2, target: (int)TaskField.A, a: 0, b: 7, c: 0)); // no contact yet
            rig.Feed(Ev.Host(LineEventType.HostMessage, 1046, actor: 3, target: 2, a: (int)MessageKind.Text, b: 1, c: 0));
            rig.Feed(Ev.Host(LineEventType.HostChoice, 1050, actor: 2, target: (int)TaskField.A, a: 1, b: 7, c: 1)); // changed after a text
            rig.Feed(Ev.Host(LineEventType.HostChoice, 1051, actor: 3, target: (int)(TaskField.A | TaskField.B), a: 2, b: 7, c: 0));
            var ev = rig.Events;
            Assert.AreEqual(0, ev.Count(e => e.Name.StartsWith("choice_")), "private events are held until match end");
            rig.Go(1099, LinePhase.RoundReveal, 10);
            rig.Go(1109, LinePhase.WeekResults, 45); // jump straight to results (flow warning is fine here)
            ev = rig.Events;
            var c0 = ev.Single(e => e.Name == "choice_submitted" && e.GetInt("player") == 0);
            Assert.AreEqual(16.0, c0.GetFloat("latency_seconds"), 1e-6);
            Assert.IsTrue(c0.GetBool("after_contact")); Assert.AreEqual("voice", c0.GetString("contact_channel")); Assert.AreEqual("a", c0.GetString("field"));
            var c2 = ev.Single(e => e.Name == "choice_submitted" && e.GetInt("player") == 2);
            Assert.IsFalse(c2.GetBool("after_contact")); Assert.AreEqual("none", c2.GetString("contact_channel"));
            var c2b = ev.Single(e => e.Name == "choice_changed" && e.GetInt("player") == 2);
            Assert.AreEqual("text", c2b.GetString("contact_channel")); Assert.AreEqual(0, c2b.GetInt("previous_category"));
            Assert.AreEqual("multiple", ev.Single(e => e.Name == "choice_submitted" && e.GetInt("player") == 3).GetString("field"));
            var pps = ev.Where(e => e.Name == "round_player_summary").ToDictionary(e => (int)e.GetInt("player"));
            Assert.AreEqual(1, pps[2].GetInt("revisions"));
            Assert.AreEqual(1, pps[3].GetInt("texts_sent"));
            Assert.AreEqual("text", pps[3].GetString("comm_mode"));
            Assert.AreEqual(1, ev.Count(e => e.Name == "message_sent"));
            var msg = ev.Single(e => e.Name == "message_sent");
            Assert.AreEqual("text", msg.GetString("channel")); Assert.AreEqual("medium", msg.GetString("length_bucket")); Assert.AreEqual(2, msg.GetInt("recipient"));
        }

        [Test]
        public void Choices_TimedOutReasons()
        {
            var rig = InRound();
            rig.Feed(Ev.Host(LineEventType.HostMissedTask, 1100, actor: 1, a: 0, b: 1));
            rig.Feed(Ev.Host(LineEventType.HostMissedTask, 1100, actor: 2, a: 0, b: 2));
            rig.Feed(Ev.Host(LineEventType.HostMissedTask, 1100, actor: 3, a: 0, b: 3));
            rig.Host.Observer.MatchEnded("results", 1200);
            var r = rig.Named("choice_timed_out").ToDictionary(e => (int)e.GetInt("player"), e => e.GetString("reason"));
            Assert.AreEqual("missed", r[1]); Assert.AreEqual("blocked", r[2]); Assert.AreEqual("absent", r[3]);
        }

        [Test]
        public void Rounds_EarlySubmitAndResolution()
        {
            var rig = InRound();
            rig.Feed(Ev.Pub(LineEventType.TaskSubmitted, 1060, actor: 0));
            rig.Feed(Ev.Pub(LineEventType.TaskSubmitted, 1062, actor: 1));
            rig.Feed(Ev.Pub(LineEventType.TaskSubmitted, 1064, actor: 2));
            rig.Feed(Ev.Pub(LineEventType.TaskSubmitted, 1066, actor: 3));
            rig.Feed(Ev.Host(LineEventType.HostEarlySubmit, 1066, a: 33000, b: 20000, c: 0));
            rig.Engine.Reveals.Add(new LineTaskReveal { Number = 0, Output = 80, MaxOutput = 100 });
            rig.Go(1074, LinePhase.RoundReveal, 10, PhaseEndReason.TimerShortened);
            rig.Feed(Ev.Pub(LineEventType.TaskResolved, 1074, a: 0, b: 80));
            rig.Host.MatchEnded(rig.Engine, "host_closed", 1080);
            var rf = One(rig, "round_finished");
            Assert.AreEqual(80, rf.GetInt("output")); Assert.AreEqual(100, rf.GetInt("max_output")); Assert.AreEqual(4, rf.GetInt("submitted"));
            Assert.IsTrue(rf.GetBool("early_submit")); Assert.AreEqual(33.0, rf.GetFloat("saved_seconds")); Assert.AreEqual(20.0, rf.GetFloat("carried_seconds"));
            var pps = rig.Named("round_player_summary").ToDictionary(e => (int)e.GetInt("player"));
            Assert.AreEqual(1074 - 1060, pps[0].GetFloat("post_submit_wait_seconds"), 1e-6);
            Assert.AreEqual(1074 - 1066, pps[3].GetFloat("post_submit_wait_seconds"), 1e-6);
            Assert.IsTrue(pps[0].GetBool("submitted"));
            Assert.AreEqual(1, rig.Named("task_output_resolved").Count());
        }

        [Test]
        public void Rounds_HostOutcomeDetailsAttachToTaskOutputAndChoiceOutcome()
        {
            var rig = InRound();
            rig.Engine.Reveals.Add(new LineTaskReveal { Number = 0, Output = 70, MaxOutput = 100 });
            rig.Go(1099, LinePhase.RoundReveal, 10);
            // Same batch: the public resolution, the per-task detail and per-participant outcomes.
            rig.Feed(Ev.Pub(LineEventType.TaskResolved, 1099, a: 0, b: 70),
                Ev.Host(LineEventType.HostTaskOutcome, 1099, a: 0, b: 30, c: 21),
                Ev.Host(LineEventType.HostOutcome, 1099, actor: 1, target: 0, a: 100, b: 2, c: 1 | 2 | 4),
                Ev.Host(LineEventType.HostOutcome, 1099, actor: 2, target: 0, a: 0, b: 0, c: 1 | 4));
            var to = One(rig, "task_output_resolved");
            Assert.AreEqual(70, to.GetInt("output")); Assert.AreEqual(30, to.GetInt("sabotage_loss"));
            Assert.AreEqual(2, to.GetInt("groups")); Assert.AreEqual(1, to.GetInt("groups_full"));
            // Per-player outcomes are private until the match ends.
            Assert.AreEqual(0, rig.Named("choice_outcome_resolved").Count());
            rig.Host.Observer.MatchEnded("results", 1200);
            var co = rig.Named("choice_outcome_resolved").ToDictionary(e => (int)e.GetInt("player"));
            Assert.AreEqual(100, co[1].GetInt("quality")); Assert.IsTrue(co[1].GetBool("cooperation_credit"));
            Assert.AreEqual(0, co[2].GetInt("quality")); Assert.IsFalse(co[2].GetBool("cooperation_credit")); Assert.IsTrue(co[2].GetBool("cooperation_opportunity"));
        }

        [Test]
        public void Rounds_TaskOutputWithoutDetailIsStillWrittenOnce()
        {
            var rig = InRound();
            rig.Go(1099, LinePhase.RoundReveal, 10);
            rig.Feed(Ev.Pub(LineEventType.TaskResolved, 1099, a: 0, b: 40));
            rig.Feed(Ev.Host(LineEventType.HostTaskOutcome, 1099, a: 0, b: 0, c: 11)); // late detail: no duplicate
            var to = One(rig, "task_output_resolved");
            Assert.IsFalse(to.TryGet("sabotage_loss", out _));
        }

        [Test]
        public void Rounds_WithoutEarlySubmitOmitsSavedSeconds()
        {
            var rig = InRound();
            rig.Go(1099, LinePhase.RoundReveal, 10);
            rig.Feed(Ev.Pub(LineEventType.TaskResolved, 1099, a: 0, b: 40));
            var rf = One(rig, "round_finished");
            Assert.IsFalse(rf.GetBool("early_submit"));
            Assert.IsFalse(rf.TryGet("saved_seconds", out _));
            Assert.AreEqual(0, rf.GetInt("submitted"));
        }

        // ---- meetings ----------------------------------------------------------------------------

        static MatchRig ToVote(out double t)
        {
            var rig = InRound();
            rig.Go(1099, LinePhase.RoundReveal, 10);
            rig.Go(1109, LinePhase.Break, 15);
            rig.Go(1124, LinePhase.MeetingDossier, 24);
            rig.Engine.Meeting = new LineMeetingView { IsFinal = false };
            rig.Go(1148, LinePhase.MeetingDiscussion, 50);
            rig.Go(1198, LinePhase.MeetingVote, 20);
            t = 1198;
            return rig;
        }

        [Test]
        public void Votes_AreWrittenAtVerdictAndResolveOnce()
        {
            double t;
            var rig = ToVote(out t);
            rig.Engine.Votes[0] = 2; rig.Feed(Ev.Pub(LineEventType.VoteCast, 1200, actor: 0));
            rig.Engine.Votes[1] = LineIds.Nobody; rig.Feed(Ev.Pub(LineEventType.VoteCast, 1201, actor: 1));
            rig.Engine.Votes[0] = 3; rig.Feed(Ev.Pub(LineEventType.VoteCast, 1203, actor: 0)); // changed her mind
            Assert.AreEqual(0, rig.Named("meeting_vote_submitted").Count(), "ballots are not visible in the log while voting");
            rig.Feed(Ev.Host(LineEventType.HostBallot, 1218, target: 3, a: 1, b: (int)VoteOutcome.BelowMinimum, c: 1));
            rig.Feed(Ev.Host(LineEventType.HostBallot, 1218, target: 3, a: 1, b: (int)VoteOutcome.BelowMinimum, c: 1)); // duplicate delivery
            rig.Go(1218, LinePhase.MeetingVerdict, 6);
            rig.Feed(Ev.Pub(LineEventType.WarningGiven, 1218, target: 3));
            rig.Feed(Ev.Pub(LineEventType.WarningGiven, 1218, target: 3));
            var votes = rig.Named("meeting_vote_submitted").ToList();
            Assert.AreEqual(3, votes.Count);
            Assert.AreEqual(2, votes[0].GetInt("target")); Assert.IsFalse(votes[0].GetBool("changed"));
            Assert.AreEqual("no_action", votes[1].GetString("target"));
            Assert.AreEqual(3, votes[2].GetInt("target")); Assert.IsTrue(votes[2].GetBool("changed"));
            var res = One(rig, "meeting_vote_resolved");
            Assert.AreEqual("below_minimum", res.GetString("outcome")); Assert.AreEqual(1, res.GetInt("top_votes")); Assert.AreEqual(1, res.GetInt("no_action_votes"));
            Assert.AreEqual(4, res.GetInt("eligible"));
            Assert.AreEqual(1, rig.Named("warning_assigned").Count());
        }

        [Test]
        public void Warning_ReportsBlameAndIsOnePerTarget()
        {
            double t;
            var rig = ToVote(out t);
            rig.Feed(Ev.Pub(LineEventType.BlameChanged, 1218, target: 3, a: 3, b: (int)BlameReason.Warning));
            rig.Engine.Blame[3] = 8;
            rig.Go(1218, LinePhase.MeetingVerdict, 6);
            rig.Feed(Ev.Pub(LineEventType.WarningGiven, 1218, target: 3));
            var w = One(rig, "warning_assigned");
            Assert.AreEqual(8, w.GetInt("blame_after")); Assert.AreEqual(5, w.GetInt("blame_before"));
            var bc = One(rig, "fault_points_changed");
            Assert.AreEqual("warning", bc.GetString("reason")); Assert.AreEqual(3, bc.GetInt("delta")); Assert.AreEqual(8, bc.GetInt("blame_after"));
        }

        [Test]
        public void Meeting_StartsFinishesAndCountsDiscussion()
        {
            var rig = InRound();
            rig.Go(1099, LinePhase.RoundReveal, 10);
            rig.Go(1109, LinePhase.Break, 15);
            rig.Go(1124, LinePhase.MeetingDossier, 24);
            rig.Engine.Meeting = new LineMeetingView { IsFinal = false };
            rig.Go(1148, LinePhase.MeetingDiscussion, 50);
            rig.Feed(Ev.Host(LineEventType.HostMessage, 1160, actor: 1, target: -1, a: (int)MessageKind.MeetingChat, b: 0, c: 0));
            rig.Feed(Ev.Host(LineEventType.HostMessage, 1162, actor: 2, target: -1, a: (int)MessageKind.MeetingChat, b: 0, c: 0));
            rig.Go(1198, LinePhase.MeetingVote, 20);
            rig.Go(1218, LinePhase.MeetingVerdict, 6);
            rig.Go(1224, LinePhase.Morning, 12, PhaseEndReason.Timer, -1, -1, 1);
            var d = One(rig, "meeting_discussion_finished");
            Assert.AreEqual(50.0, d.GetFloat("duration_seconds"), 1e-6); Assert.AreEqual(2, d.GetInt("chat_messages"));
            Assert.AreEqual(100.0, One(rig, "meeting_finished").GetFloat("duration_seconds"), 1e-6);
            Assert.IsFalse(One(rig, "meeting_started").GetBool("is_final"));
            var df = One(rig, "day_finished");
            Assert.AreEqual(0, df.GetInt("day_output"));
            Assert.AreEqual(700, df.GetInt("provisional_permille"));
        }

        // ---- final / results ---------------------------------------------------------------------

        static LineWeekResult Result(bool closed, int fired)
        {
            var r = new LineWeekResult { WeekId = 1, MatchId = "m-test0001", Days = 3, Fired = fired, CompanyClosed = closed, FinalProductivityPermille = closed ? 400 : 720, TargetPercent = 65, IncidentsTotal = 3 };
            r.FinalOutcome = closed ? VoteOutcome.CompanyClosed : VoteOutcome.Fired;
            for (int i = 0; i < 4; i++)
            {
                var p = new LineWeekPlayerResult { Player = i, Fired = i == fired, Objective = ObjectiveKind.Saver, ObjectiveDone = i % 2 == 0, Rank = i + 1, Money = 6 + i, CareerTotal = 100 + i, ContributionQuality = i == 0 ? 70 : -1 };
                if (!closed)
                {
                    p.Career.Add(new LineCareerLine { Item = CareerItem.NotFired, Points = i == fired ? 0 : 100 });
                    p.Career.Add(new LineCareerLine { Item = CareerItem.CompanyTarget, Points = 75 });
                    p.Career.Add(new LineCareerLine { Item = CareerItem.ProvenSabotage, Points = -50 });
                }
                r.Players.Add(p);
            }
            r.Exposed.Add(new LineIncidentView { Id = 1, Kind = IncidentKind.InvoicePadding, Actor = 1 });
            return r;
        }

        [Test]
        public void Final_CompletionAndSharedDefeatAreDistinct()
        {
            // ---- completed match: somebody is fired
            var rig = InRound();
            rig.Go(1099, LinePhase.RoundReveal, 10); rig.Go(1109, LinePhase.Break, 15); rig.Go(1124, LinePhase.MeetingDossier, 24);
            rig.Engine.Meeting = new LineMeetingView { IsFinal = true, Roster = new List<int> { 0, 1, 2, 3 }, MajorityNeeded = 3, DefenseOrder = new List<int> { 2, 1, 0, 3 } };
            rig.Go(1148, LinePhase.MeetingDiscussion, 75);
            rig.Go(1223, LinePhase.FinalDefense, 60);
            rig.Engine.Defender = 2; rig.Host.EngineTick(rig.Engine, 1224);
            rig.Engine.Defender = 1; rig.Host.EngineTick(rig.Engine, 1240);
            rig.Go(1283, LinePhase.FinalVote, 25);
            rig.Engine.Votes[0] = 2; rig.Feed(Ev.Pub(LineEventType.VoteCast, 1290, actor: 0));
            rig.Engine.Votes[1] = LineIds.Nobody; rig.Feed(Ev.Pub(LineEventType.VoteCast, 1291, actor: 1));
            rig.Feed(Ev.Host(LineEventType.HostBallot, 1308, target: 2, a: 3, b: (int)VoteOutcome.Fired, c: 1));
            rig.Feed(Ev.Pub(LineEventType.Fired, 1308, target: 2));
            rig.Go(1308, LinePhase.FinalVerdict, 10);
            rig.Engine.Result = Result(false, 2);
            rig.Go(1318, LinePhase.WeekResults, 45);
            rig.Host.MatchEnded(rig.Engine, "results", 1363);
            var done = rig.Events;
            var wc = done.Single(e => e.Name == "week_completed");
            Assert.IsFalse(wc.GetBool("company_closed")); Assert.IsTrue(wc.GetBool("fired"));
            Assert.AreEqual(1, done.Count(e => e.Name == "final_vote_resolved"));
            var fv = done.Single(e => e.Name == "final_vote_resolved");
            Assert.AreEqual("fired", fv.GetString("outcome")); Assert.AreEqual(4, fv.GetInt("roster")); Assert.AreEqual(3, fv.GetInt("majority_needed")); Assert.AreEqual(1, fv.GetInt("fire_nobody_votes"));
            Assert.AreEqual(1, done.Count(e => e.Name == "player_fired"));
            Assert.AreEqual(2, done.Count(e => e.Name == "final_vote_submitted"));
            Assert.AreEqual(2, done.Count(e => e.Name == "defense_started"));
            Assert.AreEqual(2, done.Count(e => e.Name == "defense_finished"), "one when the next defender took over, one when the phase ended");
            Assert.AreEqual(0, done.Count(e => e.Name == "company_failed"));
            Assert.AreEqual(4, done.Count(e => e.Name == "career_breakdown"));
            var cb = done.Single(e => e.Name == "career_breakdown" && e.GetInt("player") == 0);
            Assert.AreEqual(100, cb.GetInt("not_fired")); Assert.AreEqual(75, cb.GetInt("company_target")); Assert.AreEqual(50, cb.GetInt("sabotage_penalty")); Assert.AreEqual(70, cb.GetInt("quality"));
            Assert.IsFalse(done.Single(e => e.Name == "career_breakdown" && e.GetInt("player") == 1).TryGet("quality", out _));
            Assert.AreEqual(1, done.Count(e => e.Name == "company_result"));
            Assert.IsFalse(done.Single(e => e.Name == "company_result").GetBool("closed"));
            Assert.AreEqual(4, done.Count(e => e.Name == "objective_completed"));
            Assert.AreEqual(1, done.Count(e => e.Name == "week_finished"));

            // ---- shared defeat: the company closed
            var rig2 = InRound();
            rig2.Go(1099, LinePhase.RoundReveal, 10); rig2.Go(1109, LinePhase.Break, 15); rig2.Go(1124, LinePhase.MeetingDossier, 24);
            rig2.Engine.Meeting = new LineMeetingView { IsFinal = true };
            rig2.Go(1148, LinePhase.MeetingDiscussion, 75);
            rig2.Feed(Ev.Pub(LineEventType.CompanyClosed, 1223, a: 400));
            rig2.Go(1223, LinePhase.FinalVerdict, 10, PhaseEndReason.Skipped);
            rig2.Engine.Result = Result(true, LineIds.None);
            rig2.Go(1233, LinePhase.WeekResults, 45);
            rig2.Host.MatchEnded(rig2.Engine, "results", 1278);
            var lost = rig2.Events;
            Assert.AreEqual(1, lost.Count(e => e.Name == "company_failed"));
            Assert.AreEqual(400, lost.Single(e => e.Name == "company_failed").GetInt("final_permille"));
            Assert.IsTrue(lost.Single(e => e.Name == "final_started").GetBool("company_closed"));
            Assert.AreEqual(0, lost.Count(e => e.Name == "final_vote_resolved"));
            Assert.AreEqual(0, lost.Count(e => e.Name == "player_fired"));
            Assert.AreEqual(0, lost.Count(e => e.Name == "career_breakdown"), "no career lines or ranks when the company closed");
            var wc2 = lost.Single(e => e.Name == "week_completed");
            Assert.IsTrue(wc2.GetBool("company_closed")); Assert.IsFalse(wc2.GetBool("fired"));
            Assert.IsTrue(lost.Single(e => e.Name == "company_result").GetBool("closed"));
            Assert.AreEqual("skipped", lost.Single(e => e.Name == "phase_exited" && e.Phase == "meeting_discussion").GetString("completion_reason"));
        }

        [Test]
        public void Abandon_ReportsOnceAndReleasesPrivateEvents()
        {
            var rig = InRound();
            rig.Feed(Ev.Host(LineEventType.HostChoice, 1040, actor: 0, target: 2, a: 1, b: 0, c: 0));
            Assert.AreEqual(0, rig.Named("choice_submitted").Count());
            rig.Engine.Disconnected[2] = true; rig.Engine.Disconnected[3] = true; rig.Engine.Disconnected[1] = true;
            rig.Host.PlayerConnection(1, false, 1050); rig.Host.PlayerConnection(2, false, 1050); rig.Host.PlayerConnection(3, false, 1050);
            rig.Go(1110, LinePhase.Abandoned, 0, PhaseEndReason.Abandoned);
            rig.Host.MatchEnded(rig.Engine, "abandoned", 1111);
            var ab = One(rig, "match_abandoned");
            Assert.AreEqual("round_play", ab.GetString("phase")); Assert.AreEqual(1, ab.GetInt("connected"));
            Assert.AreEqual(1, rig.Named("choice_submitted").Count(), "private buffer flushed at abandon");
            var wf = One(rig, "week_finished");
            Assert.AreEqual("abandoned", wf.GetString("completion_reason"));
            Assert.AreEqual("abandoned", rig.Named("phase_exited").Single(e => e.Phase == "round_play").GetString("completion_reason"));
            Assert.AreEqual(1, rig.Named("round_player_summary").Count(e => e.GetInt("player") == 0), "summary emitted when the window closed");
        }

        // ---- economy / incidents -----------------------------------------------------------------

        [Test]
        public void Economy_EventsAreEmittedWithoutLeakingBeforeResults()
        {
            var rig = InRound();
            rig.Feed(Ev.Host(LineEventType.HostIncome, 1100, actor: 1, a: 2, b: (int)MoneyReason.TaskIncome, c: 3));
            rig.Feed(Ev.Host(LineEventType.HostIncident, 1100, actor: 1, target: 2, a: (int)IncidentKind.InvoicePadding, b: 11, c: 0));
            rig.Feed(Ev.Pub(LineEventType.TransferMade, 1101, actor: 0, target: 3, a: 5));
            rig.Feed(new LineEvent { Type = LineEventType.PurchaseMade, Time = 1102, Audience = LineAudience.Player, AudiencePlayer = 3, A = 201, B = 3 });
            rig.Feed(Ev.Pub(LineEventType.IncidentExposed, 1150, target: 1, a: 11, b: (int)IncidentKind.InvoicePadding));
            var before = rig.Events;
            Assert.IsFalse(before.Any(e => e.IsPrivate), "income, incident, transfer and purchase wait for the match end");
            Assert.AreEqual(1, before.Count(e => e.Name == "sabotage_verified"));
            var sv = before.Single(e => e.Name == "sabotage_verified");
            Assert.AreEqual(50.0, sv.GetFloat("seconds_since_incident"), 1e-6);
            rig.Host.MatchEnded(rig.Engine, "host_closed", 1200);
            var after = rig.Events;
            Assert.AreEqual(2, after.Single(e => e.Name == "income_credited").GetInt("amount"));
            Assert.AreEqual(3, after.Single(e => e.Name == "income_credited").GetInt("raw_amount"));
            Assert.AreEqual(2, after.Single(e => e.Name == "income_credited").GetInt("block_total") );
            Assert.AreEqual("invoice_padding", after.Single(e => e.Name == "incident_created").GetString("kind"));
            Assert.AreEqual(2, after.Single(e => e.Name == "incident_created").GetInt("victim"));
            var tr = after.Single(e => e.Name == "money_transferred");
            Assert.AreEqual(5, tr.GetInt("amount")); Assert.AreEqual("round_play", tr.GetString("phase"));
            var pu = after.Single(e => e.Name == "evidence_purchased");
            Assert.AreEqual("task_audit", pu.GetString("offer_kind")); Assert.AreEqual(1, pu.GetInt("purchase_index")); Assert.AreEqual(3, pu.GetInt("price"));
        }

        [Test]
        public void Productivity_MeasuringIsSkippedNeverZero()
        {
            var rig = InRound();
            rig.Feed(Ev.Pub(LineEventType.ProductivityChanged, 1100, a: -1, b: 0));
            rig.Feed(Ev.Pub(LineEventType.ProductivityChanged, 1110, a: 640, b: -40));
            var p = One(rig, "productivity_changed");
            Assert.AreEqual(640, p.GetInt("provisional_permille")); Assert.AreEqual(-40, p.GetInt("delta_permille")); Assert.AreEqual("task", p.GetString("change"));
        }

        // ---- recordings --------------------------------------------------------------------------

        [Test]
        public void Recordings_StartAndFinishAreLinked()
        {
            var rig = InRound();
            rig.Feed(Ev.Connected(1030, 0, 1, 7, 0));
            rig.Feed(Ev.Ended(1050, 0, 1, CallEndReason.HungUp, 7));
            rig.Feed(Ev.Host(LineEventType.HostRecordingPick, 1130, a: 0, b: 2, c: (int)RecordingSelection.Context));
            var pick = new LineRecordingPick { Index = 0, Caller = 0, Callee = 1, Context = TaskKind.InvoiceReconcile };
            pick.CandidateCallIds.Add(7); pick.CandidateCallIds.Add(8);
            rig.Host.RecordingStarted(rig.Engine, pick, 7, new TelemetryRecordingInfo { Candidates = 2, ChosenRank = 0, SpeechMs = 5200, SnippetMs = 7000, CallLengthMs = 20000, Failure = "none" }, 1140);
            rig.Host.RecordingFinished(7, 6.9, false, 1147);
            var pick2 = new LineRecordingPick { Index = 1, Context = TaskKind.InvoiceReconcile };
            rig.Host.RecordingStarted(rig.Engine, pick2, -1, new TelemetryRecordingInfo { ChosenRank = -1, Failure = "no_candidates" }, 1150);
            var s = One(rig, "recording_playback_started");
            Assert.AreEqual(2, s.GetInt("candidates")); Assert.AreEqual(0, s.GetInt("chosen_rank")); Assert.AreEqual(5200, s.GetInt("speech_ms"));
            Assert.AreEqual(0, s.GetInt("caller")); Assert.AreEqual(1, s.GetInt("callee")); Assert.IsTrue(s.GetBool("same_group")); Assert.AreEqual(0, s.GetInt("prior_exposures"));
            Assert.AreEqual("context", s.GetString("selection")); Assert.AreEqual("invoice_reconcile", s.GetString("context_task"));
            var fin = rig.Named("recording_playback_finished").OrderBy(e => e.GetInt("pick_index")).ToList();
            Assert.AreEqual(2, fin.Count);
            Assert.IsTrue(fin[0].GetBool("played")); Assert.AreEqual(6.9, fin[0].GetFloat("actual_duration_seconds"), 1e-6); Assert.AreEqual("none", fin[0].GetString("failure"));
            Assert.IsFalse(fin[1].GetBool("played")); Assert.AreEqual("no_candidates", fin[1].GetString("failure")); Assert.IsFalse(fin[1].TryGet("actual_duration_seconds", out _));
            Assert.AreEqual(1, rig.Named("phone_call_finished").Count());
        }

        // ---- resilience --------------------------------------------------------------------------

        [Test]
        public void Observer_NeverThrowsIntoTheHost()
        {
            var rig = InRound();
            rig.Engine.ViewFn = (p, now) => throw new InvalidOperationException("engine exploded with secret text");
            Assert.DoesNotThrow(() =>
            {
                rig.Feed(Ev.Host(LineEventType.HostChoice, 1040, actor: 0, target: 2, a: 1, b: 0, c: 0));
                rig.Feed(Ev.Pub(LineEventType.VoteCast, 1041, actor: 0));
                rig.Go(1099, LinePhase.RoundReveal, 10);
                rig.Host.MatchEnded(rig.Engine, "results", 1200);
            });
            var failures = rig.Named("technical_failure").ToList();
            Assert.Greater(failures.Count, 0);
            Assert.AreEqual("InvalidOperationException", failures[0].GetString("type"));
            Assert.IsFalse(failures.Any(f => TelemetryJson.ToLine(f).Contains("secret")), "exception messages are never recorded");
        }

        [Test]
        public void Observer_IgnoresEventsAfterEndAndGarbage()
        {
            var rig = InRound();
            rig.Host.MatchEnded(rig.Engine, "results", 1200);
            int n = rig.Events.Count;
            Assert.DoesNotThrow(() => rig.Host.EngineEvents(rig.Engine, new List<LineEvent> { null, Ev.Phase(1300, LinePhase.Morning) }, 1300));
            Assert.AreEqual(n, rig.Events.Count);
            Assert.DoesNotThrow(() => rig.Host.MatchEnded(rig.Engine, "results", 1300));
            Assert.AreEqual(1, rig.Named("week_finished").Count());
        }

        [Test]
        public void Observer_DoesNotReadEngineWhenDisabled()
        {
            var h = new Harness(c => c.Enabled = false, 3, true);
            var eng = new FakeEngine(4);
            var host = new HostTelemetryCoordinator(h.Service, () => h.Clock);
            host.MatchStarted(eng, eng.Setup, null, 10);
            int before = eng.ViewCalls;
            host.EngineEvents(eng, new List<LineEvent> { Ev.Phase(11, LinePhase.Morning) }, 11);
            host.EngineTick(eng, 12);
            Assert.AreEqual(before, eng.ViewCalls, "a disabled service costs the engine nothing");
        }

        [Test]
        public void Tracker_UnionsOverlapsAndClips()
        {
            var t = new CallIntervalTracker();
            t.Open(1, 0, 1, 10); t.Open(2, 0, 2, 20);
            CallInterval c;
            Assert.IsTrue(t.Close(1, 40, false, out c));
            Assert.IsTrue(t.Close(2, 50, false, out c));
            Assert.IsFalse(t.Close(2, 60, false, out c), "second close of a pair is ignored");
            Assert.AreEqual(40.0, t.UnionSeconds(0, 0, 100, 100), 1e-9, "10..50 once, not 30 + 30");
            Assert.AreEqual(30.0, t.UnionSeconds(1, 0, 100, 100), 1e-9);
            Assert.AreEqual(10.0, t.UnionSeconds(0, 0, 20, 100), 1e-9, "clipped to the window");
            Assert.AreEqual(0.0, t.UnionSeconds(3, 0, 100, 100));
            t.Open(3, 1, 2, 90);
            Assert.AreEqual(10.0, t.UnionSeconds(1, 80, 100, 100), 1e-9, "open call runs until now");
            var closed = new List<CallInterval>();
            Assert.AreEqual(1, t.CensorPlayer(2, 95, closed));
            Assert.IsTrue(closed[0].Censored);
            Assert.IsFalse(t.Open(3, 1, 2, 91), "duplicate CallConnected is ignored");
            Assert.AreEqual(5.0 + 0, t.UnionSeconds(1, 90, 100, 100), 1e-9);
        }
    }
}
