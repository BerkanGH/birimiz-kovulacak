using System;
using System.Collections.Generic;
using BirimizKovulacak.Core;

namespace BirimizKovulacak.Telemetry
{
    /// <summary>
    /// Single source of truth for every analytics event (docs/ANALYTICS_ARCHITECTURE.md §5). docs/analytics/event_catalog.json
    /// and docs/ANALYTICS_EVENT_CATALOG.md are generated from this table (Tools/TelemetryTests, CatalogExport test).
    ///
    /// Row: name | authority H/C/B | sensitivity P/M | consent S/A/V/H | routing L/LC | params | availability | max frequency | kpi tags | meaning | trigger | notes
    /// Param: [*]type:name[?][(values)]  type i int, f float, b bool, e enum, id id, s slot, t slot-or-enum; * = allowed in the cloud payload; ? = optional.
    /// Enum values: a|b|c or @Source. Availability: I implemented, P:&lt;missing source&gt;, U:&lt;reason&gt;, N:&lt;reason&gt;.
    /// Trigger prefixes: obs: = Core/Telemetry/LineTelemetryObserver.cs, hc: = Core/Telemetry/HostTelemetryCoordinator.cs, cc: = Core/Telemetry/ClientConnectionTelemetry.cs, ct: = Scripts/Telemetry/ClientTelemetry.cs, svc: = Core/Telemetry/GameTelemetryService.cs.
    /// </summary>
    public static class TelemetryCatalog
    {
        const string Failures = "none|no_candidates|no_speech|disabled|voice_unavailable|stopped";

        static readonly string[] Rows =
        {
            // ---- 5.1 session / lobby / match
            "session_started|C|P|S|LC|*e:platform(windows|mac|linux|editor|other),*e:language(tr|en|other),b:fullscreen,b:first_launch|I|1/session|retention,funnel|A play session of this install began (emitted once consent >= local).|ct:TelemetryBootstrap/ClientTelemetry.EmitSessionStarted|Not emitted before consent: the first launch prompt answer triggers it.",
            "session_ended|C|P|S|LC|*f:duration_seconds,*e:reason(quit|error)|I|1/session|retention|The process is quitting.|ct:ClientTelemetry.OnApplicationQuit|",
            "session_interrupted|C|P|S|L|e:reason(host_lost|reconnect_failed|transport_failed|incompatible|kicked),e:phase(@PhaseLobby),b:mid_match|I|<=1/connection loss|connection|The connection to the host was lost for good or refused after joining.|cc:ClientConnectionTelemetry.OnState|Only terminal outcomes; reconnect flip-flops are debounced.",
            "lobby_created|H|P|H|L|e:length(short|full),b:recordings_enabled|I|1/hosting session|funnel|The host opened a lobby.|hc:HostTelemetryCoordinator.LobbyCreated (via HostTelemetryBridge.LobbyCreated)|Needs the HostLineController call site.",
            "lobby_join_attempted|C|P|S|L|e:kind(manual|rejoin|auto)|I|1/attempt|funnel,connection|A join attempt started.|cc:ClientConnectionTelemetry.OnState (Connecting)|kind is manual unless ClientTelemetry.NoteJoinKind said otherwise (auto = -autojoin launch arg).",
            "lobby_joined|C|P|S|L|b:reconnect,f:wait_seconds|I|1/join|funnel,connection|The client is connected to a lobby.|cc:ClientConnectionTelemetry.OnState (Connected)|",
            "lobby_join_failed|C|P|S|LC|*e:reason(reject.version|reject.full|reject.in_progress|net.connect_failed|net.host_lost|net.reconnect_failed)|I|1/failed attempt|funnel,connection|A join or rejoin ended in failure.|cc:ClientConnectionTelemetry.OnState (terminal Failed)|Other reject.* reasons are reported as net.connect_failed.",
            "lobby_left|C|P|S|L|e:reason(user|host_closed|error),f:seconds_in_lobby|I|1/session|retention|The client left the lobby.|cc:ClientConnectionTelemetry.OnState|",
            "player_ready_changed|H|P|S|L|s:slot,b:ready,f:seconds_since_join|I|<=10/lobby|funnel|A seat changed its ready flag.|hc:HostTelemetryCoordinator.PlayerReadyChanged|Clock = telemetry monotonic clock (lobby has no engine time).",
            "match_start_attempted|H|P|H|L|i:humans,i:bots,i:ready|I|1/start press|funnel|The host pressed start.|hc:HostTelemetryCoordinator.MatchStartAttempted|",
            "match_started|H|P|H|LC|*i:humans,*i:bots,*e:length(short|full),*e:manager(@Manager),b:recordings_enabled,f:lobby_fill_seconds?,f:ready_seconds?|I|1/match|funnel|The engine was created.|hc:HostTelemetryCoordinator.MatchStarted|lobby_fill_seconds = lobby created to last seat joined; ready_seconds = all ready to start (omitted when not observed).",
            "match_start_failed|H|P|H|L|e:reason(lobby.need_players|lobby.need_ready|lobby.pool_too_small|engine_error)|I|rare|funnel|The match could not start.|hc:HostTelemetryCoordinator.MatchStartFailed|Unknown reason keys map to engine_error.",
            "match_abandoned|H|P|H|L|e:phase(@Phase),i:connected|I|rare|connection|Fewer than 2 players stayed connected; the engine abandoned the match.|obs:MatchEnded (reason abandoned)|",
            "rematch_requested|C|P|S|L|f:seconds_after_results|P:UI call site TelemetryHooks.RematchRequested (results screen) missing|1/results|retention|The player pressed play again.|ct:ClientTelemetry.RematchRequested|",
            "rematch_started|H|P|H|LC|id:previous_match_id(match),*i:returning_humans,*b:auto,*i:requests|I|1/rematch|retention|A new match began from the same lobby.|hc:HostTelemetryCoordinator.RematchStarted|Needs the HostLineController call site.",

            // ---- 5.2 progression
            "week_started|H|P|H|L|i:days,i:tasks|I|1/match|pacing|Match opened.|obs:Start|days from engine view, tasks = Company.TasksScheduled.",
            "day_started|H|P|H|L|e:task_a(@Task),e:task_b(@Task)|I|1/day|pacing|A work block (Morning) began.|obs:OnPhaseChanged (Morning)|",
            "phase_entered|H|P|H|L|f:planned_duration_seconds?|I|~12/day|pacing|A phase began.|obs:OnPhaseChanged|planned = engine.PhaseEndTime - phase start (omitted when the engine already moved on).",
            "phase_exited|H|P|H|LC|*f:duration_seconds,f:planned_duration_seconds?,*e:completion_reason(timer|timer_shortened|host_continue|abandoned|skipped),f:overrun_seconds?|I|~12/day|pacing,productivity|A phase ended (actual vs planned duration, why).|obs:OnPhaseChanged|completion_reason from PhaseChanged.B.",
            "round_started|H|P|H|L|e:family(pair|all|role),f:play_seconds,b:first_time|I|1/task|pacing|RoundPlay began.|obs:OnPhaseChanged (RoundPlay)|task_id/version in the envelope.",
            "round_finished|H|P|H|LC|i:output,*i:max_output,i:submitted,i:connected,b:early_submit,f:saved_seconds?,f:carried_seconds?|I|1/task|productivity,pacing|A task resolved.|obs:OnTaskResolved|output bucket for cloud is computed by the sink.",
            "day_finished|H|P|H|L|i:day_output,i:day_max,i:provisional_permille?|I|1/day|productivity|A work block ended (meeting begins).|obs:OnPhaseChanged (MeetingDossier)|",
            "week_finished|H|P|H|L|e:completion_reason(results|abandoned|host_closed),f:duration_seconds|I|1/match|pacing|The match ended (any way).|obs:MatchEnded|",
            "week_completed|H|P|H|LC|*f:duration_seconds,*b:company_closed,*b:fired|I|1/match|retention,productivity|WeekResults reached.|obs:OnPhaseChanged (WeekResults)|",

            // ---- 5.3 phone
            "phone_call_requested|H|M|A|L|s:caller,s:callee,b:partners_in_task|I|<=20/task|phone_participation|A caller dialled.|obs:OnCallIncoming|partners_in_task from the task Group of the caller.",
            "phone_call_connected|H|M|A|L|i:call_id,s:caller,s:callee,f:ring_seconds?,b:partners_in_task|I|<=10/task|phone_participation|The callee answered.|obs:OnCallConnected|",
            "phone_call_rejected|H|M|A|L|s:caller,s:callee,f:ring_seconds|I|<=5/task|phone_participation|The callee declined.|obs:OnCallEnded (Declined)|Never counts as participation.",
            "phone_call_missed|H|M|A|L|s:caller,s:callee|I|<=5/task|phone_participation|Nobody answered (ring timeout or caller hung up).|obs:OnCallEnded (Missed/HungUp while ringing)|Never counts as participation.",
            "phone_call_failed|H|M|A|L|s:caller,s:callee,e:reason(busy|dead_line|absent|wrong_phase)|I|<=5/task|phone_participation,accessibility|The call never rang.|obs:OnCallBusy / OnCallEnded|Rejected Apply() results (PhoneDead, Absent, WrongPhase) are not visible as engine events; only CallBusy/MissedCall/CallEnded are observed.",
            "phone_call_finished|H|M|A|L|i:call_id,s:caller,s:callee,f:duration_seconds,e:end_reason(@CallEnd),b:censored|I|<=10/task|phone_participation|A connected call ended.|obs:OnCallEnded|censored = ended by disconnect, telemetry stop or match end (duration is a lower bound).",
            "phone_round_summary|H|M|H|L|i:eligible,i:observed,i:participating,f:participation_rate?,i:attempts,i:connected,i:distinct_pairs,f:connected_seconds_union|I|1/task|phone_participation|Per task phone usage of observed players.|obs:SummarizeRound|participation = observed players with >= 1 successful connected call overlapping the task window / observed. rate omitted when observed = 0 (not_measured).",

            // ---- 5.4 information / choices
            "secret_info_presented|H|M|S|L|s:player,i:role,i:facts|I|<=6/task|information_asymmetry|Private facts were dealt to a player.|obs:OnPhaseChanged (RoundPlay) via BuildView|",
            "choice_ui_opened|C|P|S|L|e:task_id(@Task),f:seconds_into_phase|P:UI call site TelemetryHooks.ChoiceUiOpened missing|1/task|accessibility,pacing|The player opened the task controls.|ct:ClientTelemetry.ChoiceUiOpened|",
            "choice_submitted|H|M|S|L|s:player,e:field(target|a|b|multiple),i:category,f:latency_seconds,b:after_contact,e:contact_channel(none|voice|text|both),b:sabotage_option|I|<=12/task|information_asymmetry,communication|A player set task controls.|obs:OnHostChoice|category per docs/CONTRACTS_V4.md section 8. No lie or intent is inferred.",
            "choice_changed|H|M|S|L|s:player,e:field(target|a|b|multiple),i:category,f:latency_seconds,b:after_contact,e:contact_channel(none|voice|text|both),b:sabotage_option,i:previous_category?|I|<=6/task|information_asymmetry|A player changed an earlier submission.|obs:OnHostChoice|",
            "choice_timed_out|H|M|S|L|s:player,e:reason(missed|blocked|absent)|I|<=6/task|accessibility,blame_tension|A submission was missing at resolution.|obs:OnHostMissedTask|",
            "choice_outcome_resolved|H|M|S|L|s:player,i:quality,i:income,b:cooperation_credit,b:cooperation_opportunity|I|<=6/task|information_asymmetry|Outcome of one player's task contribution (quality, productive income, cooperation).|obs:OnHostOutcome|From engine HostOutcome (49).",
            "round_player_summary|H|M|S|L|s:player,f:connected_seconds,i:calls_attempted,i:calls_connected,i:unique_partners,i:texts_sent,i:emails_sent,f:first_contact_latency_seconds?,b:submitted,i:revisions,f:longest_gap_seconds,f:post_submit_wait_seconds?,i:busy_failures,i:unanswered_attempts,e:comm_mode(none|voice|text|mixed),f:blocked_seconds|I|<=6/task|communication,accessibility,phone_participation|Per player per task interaction summary.|obs:SummarizeRound|longest_gap = longest stretch of the task window without a connected call, message or submission. post_submit_wait omitted when never submitted; first_contact omitted when no contact.",

            // ---- 5.5 meetings and recordings
            "meeting_started|H|P|H|L|b:is_final,i:recordings_scheduled,i:complaints,i:incident_effects|I|<=4/match|meeting_engagement|A meeting began (MeetingDossier).|obs:OnPhaseChanged (MeetingDossier)|",
            "recording_playback_started|H|P|A|L|i:pick_index,i:candidates,i:chosen_rank,i:speech_ms,i:snippet_ms,i:call_length_ms,s:caller,s:callee,e:context_task?(@Task),b:same_group,i:prior_exposures,e:selection(legacy|context)|I|<=2/meeting|recordings|A recorded call snippet started to play in a meeting.|obs:RecordingStarted (via HostTelemetryBridge.RecordingStarted)|Needs the voice bridge call site. Audio, paths and transcripts are never recorded.",
            "recording_playback_finished|H|P|H|L|i:pick_index,b:played,f:actual_duration_seconds?,e:failure(" + Failures + ")|I|<=2/meeting|recordings|The host tape ended or no candidate could play.|obs:RecordingStarted/RecordingFinished|",
            "meeting_discussion_started|H|P|H|L||I|<=4/match|meeting_engagement|Discussion phase began.|obs:OnPhaseChanged (MeetingDiscussion)|",
            "meeting_discussion_finished|H|P|H|L|f:duration_seconds,i:chat_messages,i:presentations|I|<=4/match|meeting_engagement|Discussion phase ended.|obs:OnPhaseChanged|chat_messages counts HostMessage meeting_chat (counts only).",
            "evidence_presented|H|P|S|L|s:presenter,e:record_kind(@RecordKind),e:record_source(@RecordSource),b:exposed,b:repeat,f:record_age_seconds|I|<=2/meeting|meeting_engagement,sabotage|A record was presented in a meeting.|obs:OnEvidence (reads the presenter's view)|",
            "meeting_vote_submitted|H|P|V|L|s:voter,t:target(no_action|abstain|unconsented),b:changed|I|<=6/meeting|vote_dynamics|A daily ballot was cast.|obs:OnVoteCast|Emitted when votes become public (MeetingVerdict) so the host log never shows ballots mid-vote; event time is the cast time.",
            "meeting_vote_resolved|H|P|H|LC|*e:outcome(warning_given|capped|tie|below_minimum|no_action_won|no_votes),i:top_votes,i:no_action_votes,i:eligible,i:voted,b:target_was_blame_leader?|I|<=4/match|vote_dynamics|The daily ballot resolved.|obs:OnBallot|",
            "warning_assigned|H|P|S|L|s:target,i:blame_before?,i:blame_after,i:warnings_after|I|<=4/match|blame_tension|A warning was given.|obs:OnWarning|",
            "meeting_finished|H|P|H|L|f:duration_seconds|I|<=4/match|meeting_engagement|The meeting (dossier to verdict) ended.|obs:OnPhaseChanged|",

            // ---- 5.6 blame / final
            "fault_points_changed|H|P|S|L|s:player,i:delta,e:reason(@BlameReason),i:blame_after|I|<=20/match|blame_tension|Blame changed.|obs:OnBlameChanged|",
            "final_started|H|P|H|L|i:roster,b:company_closed|I|1/match|final|The final began (defense or closure).|obs:OnPhaseChanged|",
            "final_candidate_selected|H|P|H|L||N:no shortlist exists in the v4 ruleset|0|final|Kept for analyzer compatibility with v3.||",
            "defense_started|H|P|S|L|s:defender,i:order_index|I|<=6/match|final|A final defense turn began.|obs:Tick (polls the public view during FinalDefense)|",
            "defense_finished|H|P|S|L|s:defender,i:order_index,f:duration_seconds|I|<=6/match|final|A defense turn ended.|obs:Tick|",
            "final_vote_submitted|H|P|V|L|s:voter,t:target(fire_nobody|abstain|unconsented),b:changed|I|<=6/match|final,vote_dynamics|A final ballot was cast.|obs:OnVoteCast|Emitted at FinalVerdict (see meeting_vote_submitted).",
            "final_vote_resolved|H|P|H|LC|*e:outcome(fired|no_majority|no_votes|company_closed),i:roster,i:majority_needed,i:top_votes,i:fire_nobody_votes|I|1/match|final|The final ballot resolved.|obs:OnBallot|",
            "player_fired|H|P|S|L|s:player,i:blame,i:warnings,b:was_blame_leader|I|<=1/match|final|A player was fired.|obs:OnFired|",

            // ---- 5.7 hybrid economy and outcomes
            "task_output_resolved|H|P|H|L|i:output,i:max_output,i:sabotage_loss?,i:groups?,i:groups_full?|I|1/task|productivity|Task output resolved.|obs:OnTaskResolved + OnHostTaskOutcome|sabotage_loss, groups and groups_full come from engine HostTaskOutcome (50); omitted if that event is missing.",
            "productivity_changed|H|P|H|L|i:provisional_permille,i:delta_permille,e:change(task|remedial)|I|<=14/match|productivity|Company productivity moved.|obs:OnProductivity|Not emitted while the engine reports -1 (measuring).",
            "recovery_opportunities|H|P|H|L|i:players_with_options,i:players|I|1/break|productivity,economy|Remedial options available at a break.|obs:OnPhaseChanged (Break) via BuildView|",
            "remedial_requested|H|M|A|L|s:requester,s:cosigner|I|<=2/match|economy,blame_tension|A remedial report was requested.|obs:OnRemedialRequested|",
            "remedial_resolved|H|M|A|L|s:requester,s:cosigner,b:accepted,i:output_added?,i:blame_delta?|I|<=2/match|economy,blame_tension|A remedial request resolved.|obs:OnRemedialResolved|output_added from RemedialResolved.C.",
            "income_credited|H|M|S|L|s:player,i:amount,i:raw_amount,e:reason(@MoneyReason),i:block_total|I|<=30/match|economy|Money was credited.|obs:OnHostIncome|",
            "money_transferred|H|M|A|L|s:from,s:to,i:amount,e:phase(@Phase)|I|<=20/match|economy|A transfer happened.|obs:OnTransfer|",
            "evidence_purchased|H|M|S|L|s:buyer,e:offer_kind(@OfferKind),i:price,i:purchase_index|I|<=3/match|economy|An evidence offer was bought.|obs:OnPurchase|",
            "incident_created|H|M|S|L|s:actor,e:kind(@IncidentKind),s:victim?|I|<=10/match|sabotage|A deniable act was created.|obs:OnHostIncident|",
            "sabotage_verified|H|P|S|L|s:actor,e:kind(@IncidentKind),s:exposed_by?,f:seconds_since_incident?|I|<=10/match|sabotage|An incident was proven in a meeting.|obs:OnIncidentExposed|",
            "objective_assigned|H|M|S|L|s:player,e:kind(@ObjectiveKind)|I|<=6/match|information_asymmetry|A secret objective was dealt.|obs:Start via BuildView|",
            "objective_completed|H|M|S|L|s:player,e:kind(@ObjectiveKind),b:completed|I|<=6/match|career|Objective evaluated.|obs:EmitResults (engine.Result at WeekResults; the FinalVerdict HostObjective event is not written early)|",
            "career_breakdown|H|P|S|L|s:player,i:not_fired,i:company_target,i:objective,i:eotm,i:sabotage_penalty,i:total,i:rank,i:money_end,i:quality?|I|<=6/match|career|Career points at results.|obs:EmitResults (engine.Result)|quality omitted when the engine reports -1.",
            "company_result|H|P|H|LC|*i:final_permille,*i:target_percent,*b:closed,i:exposed_incidents,i:incidents|I|1/match|productivity|Final company outcome.|obs:EmitResults|",
            "company_failed|H|P|H|L|i:final_permille|I|<=1/match|productivity|The company closed.|obs:OnCompanyClosed|",

            // ---- 5.8 communication accessibility and diagnostics
            "message_sent|H|M|S|L|s:sender,e:channel(text|email|meeting_chat|complaint|fake_email),e:scope(one|all),s:recipient?,e:length_bucket(short|medium|long),b:is_reply,b:template,b:preset|I|<=100/match|communication,accessibility|A message was sent (length bucket only, never text).|obs:OnHostMessage|",
            "message_rejected|C|P|S|L|e:channel(text|email|meeting_chat|complaint|fake_email),e:reason(too_long|empty|rate_limited|limit_reached|wrong_phase)|I|rare|accessibility|The host refused a message.|ct:ClientTelemetry.OnResult|",
            "message_thread_opened|C|P|S|L|e:channel(text|email|meeting_chat|complaint|fake_email),i:unread|P:UI call site TelemetryHooks.MessageThreadOpened missing|<=50/match|communication|The player opened a thread.|ct:ClientTelemetry.MessageThreadOpened|",
            "ui_action_rejected|C|P|S|L|e:verb(@Verb),e:failure(@Failure)|I|<=50/match|accessibility|An action was refused by the host.|ct:ClientTelemetry.OnResult|Message verbs go to message_rejected instead.",
            "tutorial_step_viewed|C|P|S|L|e:task_id(@Task),i:step,e:action(auto|next|back|reopen)|P:UI call site TelemetryHooks.TutorialStep missing|<=60/match|onboarding|A tutorial step was shown.|ct:ClientTelemetry.TutorialStep|",
            "tutorial_closed|C|P|S|LC|*e:task_id(@Task),*i:steps_seen,f:seconds_open,*b:completed|P:UI call site TelemetryHooks.TutorialClosed missing|<=12/match|onboarding|The tutorial card closed.|ct:ClientTelemetry.TutorialClosed|",
            "onboarding_intro_completed|C|P|S|L|i:cards_seen,b:skipped|P:UI call site TelemetryHooks.OnboardingIntroCompleted missing|1/match|onboarding|The intro finished.|ct:ClientTelemetry.OnboardingIntroCompleted|",
            "settings_changed|C|P|S|L|e:setting(music_volume|sfx_volume|voice_volume|mic_gain|language|fullscreen|quality|sensitivity|other),e:value?(off|on|low|medium|high|tr|en|other),i:value_percent?|P:UI call site TelemetryHooks.SettingChanged missing|<=30/session|accessibility|A setting changed (coarse value only).|ct:ClientTelemetry.SettingChanged|",
            "player_disconnected|H|P|S|L|s:player,e:phase(@Phase)|I|<=5/match|connection|A player dropped mid-match.|hc:HostTelemetryCoordinator.PlayerConnection|Needs the HostLineController call site.",
            "player_reconnected|H|P|S|L|s:player,e:phase(@Phase),f:downtime_seconds?|I|<=5/match|connection|A player came back.|hc:HostTelemetryCoordinator.PlayerConnection|",
            "reconnect_attempted|C|P|S|L|i:attempt,f:seconds|I|<=3/interruption|connection|The client entered the reconnect loop.|cc:ClientConnectionTelemetry.OnState (Reconnecting)|",
            "reconnect_succeeded|C|P|S|L|i:attempt,f:seconds|I|<=3/interruption|connection|The reconnect worked.|cc:ClientConnectionTelemetry.OnState|",
            "reconnect_failed|C|P|S|L|i:attempt,f:seconds|I|<=3/interruption|connection|The reconnect loop gave up.|cc:ClientConnectionTelemetry.Tick (terminal)|",
            "host_departed|C|P|S|L|e:phase(@PhaseLobby),b:graceful|I|<=1/session|connection|The host went away.|cc:ClientConnectionTelemetry.OnState|graceful when the match was already over.",
            "voice_quality_summary|C|P|S|L|f:loss_pct,f:jitter_ms,i:underruns|P:VoiceSystem exposes stats only as a log string (VoiceStatsReporter.Build); a structured snapshot API is required|1/60s|connection,accessibility|Voice link quality.|ct: (not emitted)|",
            "stuck_phase|H|P|H|L|e:phase(@Phase),f:seconds_over|I|rare|telemetry_health|A phase overran its end time.|obs:Tick|Threshold 5 s past engine.PhaseEndTime, once per phase.",
            "unexpected_transition|H|P|H|L|e:from(@PhaseLobby),e:to(@PhaseLobby)|I|rare|telemetry_health|A phase change outside the documented flow.|obs:OnPhaseChanged|",
            "technical_failure|B|P|S|LC|*e:category(engine_exception|rpc_decode|voice_exception|sink_error|ui_exception),id:type(type)|I|rare|telemetry_health|A caught exception (type name only).|ct/hc:TechnicalFailure|",
            "telemetry_queue_dropped|B|P|S|L|i:dropped,e:reason(overflow|consent_withdrawn|shutdown)|I|rare|telemetry_health|Events were dropped (counted, never silent).|svc:GameTelemetryService.Flush|",
            "telemetry_sink_error|B|P|S|L|e:sink(local_jsonl|debug|unity_analytics|other),e:category(io|exception|full|unknown)|I|rare|telemetry_health|A sink threw; reported once per fault.|svc:GameTelemetryService.Flush|",
            "moment_marked|C|P|S|L|e:category(funny|betrayal|accusation|surprise|reveal|other),e:source(player)|P:UI call site TelemetryHooks.MomentMarked missing|<=20/match|retention,tension|The player marked a moment.|ct:ClientTelemetry.MomentMarked|",
            "music_cue_changed|C|P|S|L|e:cue(menu|office|meeting|final|results|tension|other),b:ducked|P:Music system call site TelemetryHooks.MusicCueChanged missing|<=30/match|accessibility|Music cue changed.|ct:ClientTelemetry.MusicCueChanged|",
            "rematch_prompt_shown|C|P|S|L||P:UI call site TelemetryHooks.RematchPromptShown missing|1/results|retention|The play again prompt appeared.|ct:ClientTelemetry.RematchPromptShown|",

            // ---- 5.9 correlation
            "player_slot_map|H|P|S|L|s:slot,id:anonymous_player_id?(player),b:is_bot|I|1/player/match|privacy|Seat to anonymous id mapping of consenting players.|hc:HostTelemetryCoordinator.MatchStarted|Only consenting players are written.",
        };

        static readonly Dictionary<string, string[]> EnumSources = BuildEnumSources();
        static readonly List<EventDef> _all = new List<EventDef>();
        static readonly Dictionary<string, EventDef> _byName = new Dictionary<string, EventDef>(StringComparer.Ordinal);

        static TelemetryCatalog()
        {
            foreach (var row in Rows)
            {
                var def = ParseRow(row);
                def.Index = _all.Count;
                _all.Add(def);
                if (_byName.ContainsKey(def.Name)) throw new InvalidOperationException("duplicate catalog event " + def.Name);
                _byName[def.Name] = def;
            }
        }

        public static IReadOnlyList<EventDef> All { get { return _all; } }
        public static int Count { get { return _all.Count; } }

        public static EventDef Find(string name)
        {
            EventDef d;
            return name != null && _byName.TryGetValue(name, out d) ? d : null;
        }

        public static EventDef Get(string name)
        {
            var d = Find(name);
            if (d == null) throw new ArgumentException("unknown telemetry event: " + name);
            return d;
        }

        static Dictionary<string, string[]> BuildEnumSources()
        {
            var phases = TelemetryNames.EnumNames<LinePhase>(true);
            var lobby = new List<string>(phases) { "lobby", "none" };
            return new Dictionary<string, string[]>
            {
                { "Phase", phases },
                { "PhaseLobby", lobby.ToArray() },
                { "Task", TelemetryNames.TaskIds() },
                { "RecordKind", TelemetryNames.EnumNames<RecordKind>(true) },
                { "RecordSource", TelemetryNames.EnumNames<RecordSource>(true) },
                { "IncidentKind", TelemetryNames.EnumNames<IncidentKind>(true) },
                { "ObjectiveKind", TelemetryNames.EnumNames<ObjectiveKind>(true) },
                { "MoneyReason", TelemetryNames.EnumNames<MoneyReason>(true) },
                { "BlameReason", TelemetryNames.EnumNames<BlameReason>(true) },
                { "OfferKind", TelemetryNames.EnumNames<OfferKind>(true) },
                { "CallEnd", TelemetryNames.EnumNames<CallEndReason>(false) },
                { "Verb", TelemetryNames.EnumNames<LineVerb>(true) },
                { "Failure", TelemetryNames.EnumNames<LineFailure>(true) },
                { "Manager", LineManagerIds.All },
            };
        }

        static EventDef ParseRow(string row)
        {
            var f = row.Split('|');
            // Enum value lists use '|' too, so the row is re-split on the parameter field by position from both ends.
            // Layout: 0 name,1 auth,2 sens,3 consent,4 route, [5..n-8] params, then avail,freq,kpi,meaning,trigger,notes (6 fields).
            int tail = 6;
            if (f.Length < 11) throw new InvalidOperationException("bad catalog row: " + row);
            var def = new EventDef { Name = f[0] };
            def.Authority = f[1] == "H" ? TelemetryAuthority.Host : f[1] == "C" ? TelemetryAuthority.Client : TelemetryAuthority.Both;
            def.Sensitivity = f[2] == "M" ? TelemetrySensitivity.PrivateUntilMatchEnd : TelemetrySensitivity.Public;
            def.Consent = f[3] == "S" ? ConsentRule.Self : f[3] == "A" ? ConsentRule.AllParticipants : f[3] == "V" ? ConsentRule.Voter : ConsentRule.HostAggregate;
            def.Routing = f[4] == "LC" ? (TelemetryRouting.Local | TelemetryRouting.Cloud) : TelemetryRouting.Local;
            int paramEnd = f.Length - tail;
            string paramField = string.Join("|", f, 5, paramEnd - 5);
            def.Params = ParseParams(paramField, def.Name);
            string avail = f[paramEnd];
            if (avail == "I") def.Availability = TelemetryAvailability.Implemented;
            else
            {
                def.Availability = avail[0] == 'P' ? TelemetryAvailability.PendingIntegration : avail[0] == 'U' ? TelemetryAvailability.Unsupported : TelemetryAvailability.NotApplicable;
                def.AvailabilityNote = avail.Substring(2);
            }
            def.MaxFrequency = f[paramEnd + 1];
            def.Kpi = f[paramEnd + 2].Length == 0 ? new string[0] : f[paramEnd + 2].Split(',');
            def.Meaning = f[paramEnd + 3];
            def.Trigger = ExpandTrigger(f[paramEnd + 4]);
            def.Notes = f[paramEnd + 5];
            return def;
        }

        static string ExpandTrigger(string t)
        {
            if (t.StartsWith("obs:")) return "Core/Telemetry/LineTelemetryObserver.cs " + t.Substring(4);
            if (t.StartsWith("hc:")) return "Core/Telemetry/HostTelemetryCoordinator.cs " + t.Substring(3);
            if (t.StartsWith("cc:")) return "Core/Telemetry/ClientConnectionTelemetry.cs " + t.Substring(3);
            if (t.StartsWith("ct:")) return "Scripts/Telemetry/ClientTelemetry.cs " + t.Substring(3);
            if (t.StartsWith("svc:")) return "Core/Telemetry/GameTelemetryService.cs " + t.Substring(4);
            return t;
        }

        static ParamDef[] ParseParams(string field, string eventName)
        {
            var list = new List<ParamDef>();
            if (field.Length == 0) return list.ToArray();
            // split on commas that are outside parentheses
            int depth = 0, start = 0;
            var parts = new List<string>();
            for (int i = 0; i < field.Length; i++)
            {
                char c = field[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0) { parts.Add(field.Substring(start, i - start)); start = i + 1; }
            }
            parts.Add(field.Substring(start));
            foreach (var raw in parts)
            {
                string s = raw;
                var p = new ParamDef();
                if (s.StartsWith("*")) { p.Cloud = true; s = s.Substring(1); }
                int colon = s.IndexOf(':');
                string type = s.Substring(0, colon);
                string rest = s.Substring(colon + 1);
                string values = null;
                int paren = rest.IndexOf('(');
                if (paren >= 0) { values = rest.Substring(paren + 1, rest.Length - paren - 2); rest = rest.Substring(0, paren); }
                if (rest.EndsWith("?")) { p.Optional = true; rest = rest.Substring(0, rest.Length - 1); }
                p.Name = rest;
                switch (type)
                {
                    case "i": p.Type = ParamType.Int; break;
                    case "f": p.Type = ParamType.Float; break;
                    case "b": p.Type = ParamType.Bool; break;
                    case "s": p.Type = ParamType.Slot; break;
                    case "e": p.Type = ParamType.Enum; SetEnum(p, values, eventName); break;
                    case "t": p.Type = ParamType.SlotOrEnum; SetEnum(p, values, eventName); break;
                    case "id":
                        p.Type = ParamType.Id;
                        p.Id = values == "player" ? IdKind.PlayerId : values == "match" ? IdKind.MatchId : IdKind.TypeName;
                        break;
                    default: throw new InvalidOperationException("bad param type in " + eventName + ": " + raw);
                }
                list.Add(p);
            }
            return list.ToArray();
        }

        static void SetEnum(ParamDef p, string values, string eventName)
        {
            if (values == null) throw new InvalidOperationException("enum without values in " + eventName + "." + p.Name);
            if (values.StartsWith("@"))
            {
                string[] set;
                if (!EnumSources.TryGetValue(values.Substring(1), out set)) throw new InvalidOperationException("unknown enum source " + values);
                p.EnumSource = values.Substring(1);
                p.EnumValues = set;
            }
            else p.EnumValues = values.Split('|');
        }
    }
}
