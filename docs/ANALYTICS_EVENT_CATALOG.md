# Analytics event catalog

Generated from `Game/Assets/Game/Core/Telemetry/TelemetryCatalog.cs` by the `CatalogExport` test in `Tools/TelemetryTests` (`TELEMETRY_EXPORT_DOCS=1 dotnet test Tools/TelemetryTests/TelemetryTests.csproj --filter CatalogExport`). Do not edit by hand. Machine-readable twin: `docs/analytics/event_catalog.json`. Contract: `docs/ANALYTICS_ARCHITECTURE.md`.

**95 events**: 35 implemented, 59 pending_integration, 0 unsupported, 1 not_applicable.

Status meaning: `implemented` = emitting code exists in this repository, fed from sources that exist today (contract events/views, connection state), covered by tests; host events still need the `HostTelemetryBridge` call sites in `HostLineController` (see `docs/ANALYTICS_SETUP.md`) and the v4 engine emitting the contract events. `pending_integration` = a source is missing (named in the entry); the event is **not** emitted. `not_applicable` / `unsupported` = never emitted.

Envelope fields (every event, omitted when not applicable): schema_version, event, event_version, event_id, authority, timestamp_utc, monotonic_elapsed_seconds, environment, session_id, match_id, week_id, anonymous_group_id, anonymous_player_id, player_slot, player_count, is_bot, has_bots, day_index, round_index, task_number, phase, task_id, task_version, game_version, build_version, ruleset_version, test_cohort, experiments, observation_coverage, params.

## Index

| Event | Authority | Sensitivity | Consent | Routing | Status |
|---|---|---|---|---|---|
| [`session_started`](#session_started) | client | public | self | local+cloud | implemented |
| [`session_ended`](#session_ended) | client | public | self | local+cloud | implemented |
| [`session_interrupted`](#session_interrupted) | client | public | self | local | implemented |
| [`lobby_created`](#lobby_created) | host | public | host_aggregate | local | implemented |
| [`lobby_join_attempted`](#lobby_join_attempted) | client | public | self | local | implemented |
| [`lobby_joined`](#lobby_joined) | client | public | self | local | implemented |
| [`lobby_join_failed`](#lobby_join_failed) | client | public | self | local+cloud | implemented |
| [`lobby_left`](#lobby_left) | client | public | self | local | implemented |
| [`player_ready_changed`](#player_ready_changed) | host | public | self | local | implemented |
| [`match_start_attempted`](#match_start_attempted) | host | public | host_aggregate | local | implemented |
| [`match_started`](#match_started) | host | public | host_aggregate | local+cloud | implemented |
| [`match_start_failed`](#match_start_failed) | host | public | host_aggregate | local | implemented |
| [`match_abandoned`](#match_abandoned) | host | public | host_aggregate | local | pending_integration |
| [`rematch_requested`](#rematch_requested) | client | public | self | local | implemented |
| [`rematch_started`](#rematch_started) | host | public | host_aggregate | local+cloud | implemented |
| [`week_started`](#week_started) | host | public | host_aggregate | local | pending_integration |
| [`day_started`](#day_started) | host | public | host_aggregate | local | pending_integration |
| [`phase_entered`](#phase_entered) | host | public | host_aggregate | local | pending_integration |
| [`phase_exited`](#phase_exited) | host | public | host_aggregate | local+cloud | pending_integration |
| [`round_started`](#round_started) | host | public | host_aggregate | local | pending_integration |
| [`round_finished`](#round_finished) | host | public | host_aggregate | local+cloud | pending_integration |
| [`day_finished`](#day_finished) | host | public | host_aggregate | local | pending_integration |
| [`week_finished`](#week_finished) | host | public | host_aggregate | local | pending_integration |
| [`week_completed`](#week_completed) | host | public | host_aggregate | local+cloud | pending_integration |
| [`phone_call_requested`](#phone_call_requested) | host | private until match end | all_participants | local | pending_integration |
| [`phone_call_connected`](#phone_call_connected) | host | private until match end | all_participants | local | pending_integration |
| [`phone_call_rejected`](#phone_call_rejected) | host | private until match end | all_participants | local | pending_integration |
| [`phone_call_missed`](#phone_call_missed) | host | private until match end | all_participants | local | pending_integration |
| [`phone_call_failed`](#phone_call_failed) | host | private until match end | all_participants | local | pending_integration |
| [`phone_call_finished`](#phone_call_finished) | host | private until match end | all_participants | local | pending_integration |
| [`phone_round_summary`](#phone_round_summary) | host | private until match end | host_aggregate | local | pending_integration |
| [`secret_info_presented`](#secret_info_presented) | host | private until match end | self | local | pending_integration |
| [`choice_ui_opened`](#choice_ui_opened) | client | public | self | local | implemented |
| [`choice_submitted`](#choice_submitted) | host | private until match end | self | local | pending_integration |
| [`choice_changed`](#choice_changed) | host | private until match end | self | local | pending_integration |
| [`choice_timed_out`](#choice_timed_out) | host | private until match end | self | local | pending_integration |
| [`choice_outcome_resolved`](#choice_outcome_resolved) | host | private until match end | self | local | pending_integration |
| [`round_player_summary`](#round_player_summary) | host | private until match end | self | local | pending_integration |
| [`meeting_started`](#meeting_started) | host | public | host_aggregate | local | pending_integration |
| [`recording_playback_started`](#recording_playback_started) | host | public | all_participants | local | pending_integration |
| [`recording_playback_finished`](#recording_playback_finished) | host | public | host_aggregate | local | pending_integration |
| [`meeting_discussion_started`](#meeting_discussion_started) | host | public | host_aggregate | local | pending_integration |
| [`meeting_discussion_finished`](#meeting_discussion_finished) | host | public | host_aggregate | local | pending_integration |
| [`evidence_presented`](#evidence_presented) | host | public | self | local | pending_integration |
| [`meeting_vote_submitted`](#meeting_vote_submitted) | host | public | voter | local | pending_integration |
| [`meeting_vote_resolved`](#meeting_vote_resolved) | host | public | host_aggregate | local+cloud | pending_integration |
| [`warning_assigned`](#warning_assigned) | host | public | self | local | pending_integration |
| [`meeting_finished`](#meeting_finished) | host | public | host_aggregate | local | pending_integration |
| [`fault_points_changed`](#fault_points_changed) | host | public | self | local | pending_integration |
| [`final_started`](#final_started) | host | public | host_aggregate | local | pending_integration |
| [`final_candidate_selected`](#final_candidate_selected) | host | public | host_aggregate | local | not_applicable |
| [`defense_started`](#defense_started) | host | public | self | local | pending_integration |
| [`defense_finished`](#defense_finished) | host | public | self | local | pending_integration |
| [`final_vote_submitted`](#final_vote_submitted) | host | public | voter | local | pending_integration |
| [`final_vote_resolved`](#final_vote_resolved) | host | public | host_aggregate | local+cloud | pending_integration |
| [`player_fired`](#player_fired) | host | public | self | local | pending_integration |
| [`task_output_resolved`](#task_output_resolved) | host | public | host_aggregate | local | pending_integration |
| [`productivity_changed`](#productivity_changed) | host | public | host_aggregate | local | pending_integration |
| [`recovery_opportunities`](#recovery_opportunities) | host | public | host_aggregate | local | pending_integration |
| [`remedial_requested`](#remedial_requested) | host | private until match end | all_participants | local | pending_integration |
| [`remedial_resolved`](#remedial_resolved) | host | private until match end | all_participants | local | pending_integration |
| [`income_credited`](#income_credited) | host | private until match end | self | local | pending_integration |
| [`money_transferred`](#money_transferred) | host | private until match end | all_participants | local | pending_integration |
| [`evidence_purchased`](#evidence_purchased) | host | private until match end | self | local | pending_integration |
| [`incident_created`](#incident_created) | host | private until match end | self | local | pending_integration |
| [`sabotage_verified`](#sabotage_verified) | host | public | self | local | pending_integration |
| [`objective_assigned`](#objective_assigned) | host | private until match end | self | local | pending_integration |
| [`objective_completed`](#objective_completed) | host | private until match end | self | local | pending_integration |
| [`career_breakdown`](#career_breakdown) | host | public | self | local | pending_integration |
| [`company_result`](#company_result) | host | public | host_aggregate | local+cloud | pending_integration |
| [`company_failed`](#company_failed) | host | public | host_aggregate | local | pending_integration |
| [`message_sent`](#message_sent) | host | private until match end | self | local | pending_integration |
| [`message_rejected`](#message_rejected) | client | public | self | local | implemented |
| [`message_thread_opened`](#message_thread_opened) | client | public | self | local | implemented |
| [`ui_action_rejected`](#ui_action_rejected) | client | public | self | local | implemented |
| [`tutorial_step_viewed`](#tutorial_step_viewed) | client | public | self | local | implemented |
| [`tutorial_closed`](#tutorial_closed) | client | public | self | local+cloud | implemented |
| [`onboarding_intro_completed`](#onboarding_intro_completed) | client | public | self | local | implemented |
| [`settings_changed`](#settings_changed) | client | public | self | local | implemented |
| [`player_disconnected`](#player_disconnected) | host | public | self | local | implemented |
| [`player_reconnected`](#player_reconnected) | host | public | self | local | implemented |
| [`reconnect_attempted`](#reconnect_attempted) | client | public | self | local | implemented |
| [`reconnect_succeeded`](#reconnect_succeeded) | client | public | self | local | implemented |
| [`reconnect_failed`](#reconnect_failed) | client | public | self | local | implemented |
| [`host_departed`](#host_departed) | client | public | self | local | implemented |
| [`voice_quality_summary`](#voice_quality_summary) | client | public | self | local | pending_integration |
| [`stuck_phase`](#stuck_phase) | host | public | host_aggregate | local | pending_integration |
| [`unexpected_transition`](#unexpected_transition) | host | public | host_aggregate | local | pending_integration |
| [`technical_failure`](#technical_failure) | both | public | self | local+cloud | implemented |
| [`telemetry_queue_dropped`](#telemetry_queue_dropped) | both | public | self | local | implemented |
| [`telemetry_sink_error`](#telemetry_sink_error) | both | public | self | local | implemented |
| [`moment_marked`](#moment_marked) | client | public | self | local | implemented |
| [`music_cue_changed`](#music_cue_changed) | client | public | self | local | implemented |
| [`rematch_prompt_shown`](#rematch_prompt_shown) | client | public | self | local | implemented |
| [`player_slot_map`](#player_slot_map) | host | public | self | local | implemented |

## Events

### session_started

- **Name / version**: `session_started` v1
- **Meaning**: A play session of this install began (emitted once consent >= local).
- **Trigger location**: `Scripts/Telemetry/ClientTelemetry.cs TelemetryBootstrap/ClientTelemetry.EmitSessionStarted`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local+cloud (cloud only for consent level 3; cloud payload = parameters marked cloud)
- **Expected max frequency**: 1/session
- **KPI contribution**: retention, funnel
- **Availability**: implemented
- **Notes**: Not emitted before consent: the first launch prompt answer triggers it.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `platform` | enum | yes | yes | windows, mac, linux, editor, other |
| `language` | enum | yes | yes | tr, en, other |
| `fullscreen` | bool | yes | no |  |
| `first_launch` | bool | yes | no |  |

### session_ended

- **Name / version**: `session_ended` v1
- **Meaning**: The process is quitting.
- **Trigger location**: `Scripts/Telemetry/ClientTelemetry.cs ClientTelemetry.OnApplicationQuit`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local+cloud (cloud only for consent level 3; cloud payload = parameters marked cloud)
- **Expected max frequency**: 1/session
- **KPI contribution**: retention
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `duration_seconds` | float (seconds unless named otherwise) | yes | yes |  |
| `reason` | enum | yes | yes | quit, error |

### session_interrupted

- **Name / version**: `session_interrupted` v1
- **Meaning**: The connection to the host was lost for good or refused after joining.
- **Trigger location**: `Core/Telemetry/ClientConnectionTelemetry.cs ClientConnectionTelemetry.OnState`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=1/connection loss
- **KPI contribution**: connection
- **Availability**: implemented
- **Notes**: Only terminal outcomes; reconnect flip-flops are debounced.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `reason` | enum | yes | no | host_lost, reconnect_failed, transport_failed, incompatible, kicked |
| `phase` | enum | yes | no | @PhaseLobby (intro, task_play, task_reveal, break, meeting_dossier, meeting_discussion, meeting_vote, meeting_verdict, crisis_calls, crisis_vote, crisis_verdict, final_discussion, final_defense, final_vote, final_verdict, results, abandoned, lobby, none) |
| `mid_match` | bool | yes | no |  |

### lobby_created

- **Name / version**: `lobby_created` v1
- **Meaning**: The host opened a lobby.
- **Trigger location**: `Core/Telemetry/HostTelemetryCoordinator.cs HostTelemetryCoordinator.LobbyCreated (via HostTelemetryBridge.LobbyCreated)`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: 1/hosting session
- **KPI contribution**: funnel
- **Availability**: implemented
- **Notes**: Needs the HostLineController call site.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `recordings_enabled` | bool | yes | no |  |

### lobby_join_attempted

- **Name / version**: `lobby_join_attempted` v1
- **Meaning**: A join attempt started.
- **Trigger location**: `Core/Telemetry/ClientConnectionTelemetry.cs ClientConnectionTelemetry.OnState (Connecting)`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: 1/attempt
- **KPI contribution**: funnel, connection
- **Availability**: implemented
- **Notes**: kind is manual unless ClientTelemetry.NoteJoinKind said otherwise (auto = -autojoin launch arg).

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `kind` | enum | yes | no | manual, rejoin, auto |

### lobby_joined

- **Name / version**: `lobby_joined` v1
- **Meaning**: The client is connected to a lobby.
- **Trigger location**: `Core/Telemetry/ClientConnectionTelemetry.cs ClientConnectionTelemetry.OnState (Connected)`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: 1/join
- **KPI contribution**: funnel, connection
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `reconnect` | bool | yes | no |  |
| `wait_seconds` | float (seconds unless named otherwise) | yes | no |  |

### lobby_join_failed

- **Name / version**: `lobby_join_failed` v1
- **Meaning**: A join or rejoin ended in failure.
- **Trigger location**: `Core/Telemetry/ClientConnectionTelemetry.cs ClientConnectionTelemetry.OnState (terminal Failed)`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local+cloud (cloud only for consent level 3; cloud payload = parameters marked cloud)
- **Expected max frequency**: 1/failed attempt
- **KPI contribution**: funnel, connection
- **Availability**: implemented
- **Notes**: Other reject.* reasons are reported as net.connect_failed.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `reason` | enum | yes | yes | reject.version, reject.full, reject.in_progress, net.connect_failed, net.host_lost, net.reconnect_failed |

### lobby_left

- **Name / version**: `lobby_left` v1
- **Meaning**: The client left the lobby.
- **Trigger location**: `Core/Telemetry/ClientConnectionTelemetry.cs ClientConnectionTelemetry.OnState`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: 1/session
- **KPI contribution**: retention
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `reason` | enum | yes | no | user, host_closed, error |
| `seconds_in_lobby` | float (seconds unless named otherwise) | yes | no |  |

### player_ready_changed

- **Name / version**: `player_ready_changed` v1
- **Meaning**: A seat changed its ready flag.
- **Trigger location**: `Core/Telemetry/HostTelemetryCoordinator.cs HostTelemetryCoordinator.PlayerReadyChanged`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=10/lobby
- **KPI contribution**: funnel
- **Availability**: implemented
- **Notes**: Clock = telemetry monotonic clock (lobby has no engine time).

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `slot` | slot (0-5) | yes | no |  |
| `ready` | bool | yes | no |  |
| `seconds_since_join` | float (seconds unless named otherwise) | yes | no |  |

### match_start_attempted

- **Name / version**: `match_start_attempted` v1
- **Meaning**: The host pressed start.
- **Trigger location**: `Core/Telemetry/HostTelemetryCoordinator.cs HostTelemetryCoordinator.MatchStartAttempted`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: 1/start press
- **KPI contribution**: funnel
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `humans` | int | yes | no |  |
| `bots` | int | yes | no |  |
| `ready` | int | yes | no |  |

### match_started

- **Name / version**: `match_started` v1
- **Meaning**: The engine was created.
- **Trigger location**: `Core/Telemetry/HostTelemetryCoordinator.cs HostTelemetryCoordinator.MatchStarted`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local+cloud (cloud only for consent level 3; cloud payload = parameters marked cloud)
- **Expected max frequency**: 1/match
- **KPI contribution**: funnel
- **Availability**: implemented
- **Notes**: lobby_fill_seconds = lobby created to last seat joined; ready_seconds = all ready to start (omitted when not observed).

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `humans` | int | yes | yes |  |
| `bots` | int | yes | yes |  |
| `recordings_enabled` | bool | yes | no |  |
| `lobby_fill_seconds` | float (seconds unless named otherwise) | optional (omitted, never 0) | no |  |
| `ready_seconds` | float (seconds unless named otherwise) | optional (omitted, never 0) | no |  |

### match_start_failed

- **Name / version**: `match_start_failed` v1
- **Meaning**: The match could not start.
- **Trigger location**: `Core/Telemetry/HostTelemetryCoordinator.cs HostTelemetryCoordinator.MatchStartFailed`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: rare
- **KPI contribution**: funnel
- **Availability**: implemented
- **Notes**: Unknown reason keys map to engine_error.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `reason` | enum | yes | no | lobby.need_players, lobby.need_ready, lobby.pool_too_small, engine_error |

### match_abandoned

- **Name / version**: `match_abandoned` v1
- **Meaning**: Fewer than 2 players stayed connected; the engine abandoned the match.
- **Trigger location**: `(removed v4) LineTelemetryObserver MatchEnded (reason abandoned)`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: rare
- **KPI contribution**: connection
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `phase` | enum | yes | no | @Phase (intro, task_play, task_reveal, break, meeting_dossier, meeting_discussion, meeting_vote, meeting_verdict, crisis_calls, crisis_vote, crisis_verdict, final_discussion, final_defense, final_vote, final_verdict, results, abandoned) |
| `connected` | int | yes | no |  |

### rematch_requested

- **Name / version**: `rematch_requested` v1
- **Meaning**: The player pressed play again.
- **Trigger location**: `Scripts/Telemetry/ClientTelemetry.cs ClientTelemetry.RematchRequested via UI: Scripts/UI/Meeting/WeekResultsView.cs (Play again)`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: 1/results
- **KPI contribution**: retention
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `seconds_after_results` | float (seconds unless named otherwise) | yes | no |  |

### rematch_started

- **Name / version**: `rematch_started` v1
- **Meaning**: A new match began from the same lobby.
- **Trigger location**: `Core/Telemetry/HostTelemetryCoordinator.cs HostTelemetryCoordinator.RematchStarted`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local+cloud (cloud only for consent level 3; cloud payload = parameters marked cloud)
- **Expected max frequency**: 1/rematch
- **KPI contribution**: retention
- **Availability**: implemented
- **Notes**: Needs the HostLineController call site.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `previous_match_id` | id (MatchId) | yes | no |  |
| `returning_humans` | int | yes | yes |  |
| `auto` | bool | yes | yes |  |
| `requests` | int | yes | yes |  |

### week_started

- **Name / version**: `week_started` v1
- **Meaning**: Match opened.
- **Trigger location**: `(removed v4) LineTelemetryObserver Start`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: 1/match
- **KPI contribution**: pacing
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: days from engine view, tasks = Company.TasksScheduled.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `days` | int | yes | no |  |
| `tasks` | int | yes | no |  |

### day_started

- **Name / version**: `day_started` v1
- **Meaning**: A work block (Morning) began.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnPhaseChanged (Morning)`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: 1/day
- **KPI contribution**: pacing
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `task_a` | enum | yes | no | @Task (client_bonus, petty_cash, weekend_shift) |
| `task_b` | enum | yes | no | @Task (client_bonus, petty_cash, weekend_shift) |

### phase_entered

- **Name / version**: `phase_entered` v1
- **Meaning**: A phase began.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnPhaseChanged`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: ~12/day
- **KPI contribution**: pacing
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: planned = engine.PhaseEndTime - phase start (omitted when the engine already moved on).

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `planned_duration_seconds` | float (seconds unless named otherwise) | optional (omitted, never 0) | no |  |

### phase_exited

- **Name / version**: `phase_exited` v1
- **Meaning**: A phase ended (actual vs planned duration, why).
- **Trigger location**: `(removed v4) LineTelemetryObserver OnPhaseChanged`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local+cloud (cloud only for consent level 3; cloud payload = parameters marked cloud)
- **Expected max frequency**: ~12/day
- **KPI contribution**: pacing, productivity
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: completion_reason from PhaseChanged.B.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `duration_seconds` | float (seconds unless named otherwise) | yes | yes |  |
| `planned_duration_seconds` | float (seconds unless named otherwise) | optional (omitted, never 0) | no |  |
| `completion_reason` | enum | yes | yes | timer, timer_shortened, host_continue, abandoned, skipped |
| `overrun_seconds` | float (seconds unless named otherwise) | optional (omitted, never 0) | no |  |

### round_started

- **Name / version**: `round_started` v1
- **Meaning**: RoundPlay began.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnPhaseChanged (RoundPlay)`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: 1/task
- **KPI contribution**: pacing
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: task_id/version in the envelope.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `family` | enum | yes | no | pair, all, role |
| `play_seconds` | float (seconds unless named otherwise) | yes | no |  |
| `first_time` | bool | yes | no |  |

### round_finished

- **Name / version**: `round_finished` v1
- **Meaning**: A task resolved.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnTaskResolved`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local+cloud (cloud only for consent level 3; cloud payload = parameters marked cloud)
- **Expected max frequency**: 1/task
- **KPI contribution**: productivity, pacing
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: output bucket for cloud is computed by the sink.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `output` | int | yes | no |  |
| `max_output` | int | yes | yes |  |
| `submitted` | int | yes | no |  |
| `connected` | int | yes | no |  |
| `early_submit` | bool | yes | no |  |
| `saved_seconds` | float (seconds unless named otherwise) | optional (omitted, never 0) | no |  |
| `carried_seconds` | float (seconds unless named otherwise) | optional (omitted, never 0) | no |  |

### day_finished

- **Name / version**: `day_finished` v1
- **Meaning**: A work block ended (meeting begins).
- **Trigger location**: `(removed v4) LineTelemetryObserver OnPhaseChanged (MeetingDossier)`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: 1/day
- **KPI contribution**: productivity
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `day_output` | int | yes | no |  |
| `day_max` | int | yes | no |  |
| `provisional_permille` | int | optional (omitted, never 0) | no |  |

### week_finished

- **Name / version**: `week_finished` v1
- **Meaning**: The match ended (any way).
- **Trigger location**: `(removed v4) LineTelemetryObserver MatchEnded`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: 1/match
- **KPI contribution**: pacing
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `completion_reason` | enum | yes | no | results, abandoned, host_closed |
| `duration_seconds` | float (seconds unless named otherwise) | yes | no |  |

### week_completed

- **Name / version**: `week_completed` v1
- **Meaning**: WeekResults reached.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnPhaseChanged (WeekResults)`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local+cloud (cloud only for consent level 3; cloud payload = parameters marked cloud)
- **Expected max frequency**: 1/match
- **KPI contribution**: retention, productivity
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `duration_seconds` | float (seconds unless named otherwise) | yes | yes |  |
| `company_closed` | bool | yes | yes |  |
| `fired` | bool | yes | yes |  |

### phone_call_requested

- **Name / version**: `phone_call_requested` v1
- **Meaning**: A caller dialled.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnCallIncoming`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `all_participants`
- **Routing**: local
- **Expected max frequency**: <=20/task
- **KPI contribution**: phone_participation
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: partners_in_task from the task Group of the caller.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `caller` | slot (0-5) | yes | no |  |
| `callee` | slot (0-5) | yes | no |  |
| `partners_in_task` | bool | yes | no |  |

### phone_call_connected

- **Name / version**: `phone_call_connected` v1
- **Meaning**: The callee answered.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnCallConnected`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `all_participants`
- **Routing**: local
- **Expected max frequency**: <=10/task
- **KPI contribution**: phone_participation
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `call_id` | int | yes | no |  |
| `caller` | slot (0-5) | yes | no |  |
| `callee` | slot (0-5) | yes | no |  |
| `ring_seconds` | float (seconds unless named otherwise) | optional (omitted, never 0) | no |  |
| `partners_in_task` | bool | yes | no |  |

### phone_call_rejected

- **Name / version**: `phone_call_rejected` v1
- **Meaning**: The callee declined.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnCallEnded (Declined)`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `all_participants`
- **Routing**: local
- **Expected max frequency**: <=5/task
- **KPI contribution**: phone_participation
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: Never counts as participation.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `caller` | slot (0-5) | yes | no |  |
| `callee` | slot (0-5) | yes | no |  |
| `ring_seconds` | float (seconds unless named otherwise) | yes | no |  |

### phone_call_missed

- **Name / version**: `phone_call_missed` v1
- **Meaning**: Nobody answered (ring timeout or caller hung up).
- **Trigger location**: `(removed v4) LineTelemetryObserver OnCallEnded (Missed/HungUp while ringing)`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `all_participants`
- **Routing**: local
- **Expected max frequency**: <=5/task
- **KPI contribution**: phone_participation
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: Never counts as participation.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `caller` | slot (0-5) | yes | no |  |
| `callee` | slot (0-5) | yes | no |  |

### phone_call_failed

- **Name / version**: `phone_call_failed` v1
- **Meaning**: The call never rang.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnCallBusy / OnCallEnded`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `all_participants`
- **Routing**: local
- **Expected max frequency**: <=5/task
- **KPI contribution**: phone_participation, accessibility
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: Rejected Apply() results (PhoneDead, Absent, WrongPhase) are not visible as engine events; only CallBusy/MissedCall/CallEnded are observed.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `caller` | slot (0-5) | yes | no |  |
| `callee` | slot (0-5) | yes | no |  |
| `reason` | enum | yes | no | busy, dead_line, absent, wrong_phase |

### phone_call_finished

- **Name / version**: `phone_call_finished` v1
- **Meaning**: A connected call ended.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnCallEnded`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `all_participants`
- **Routing**: local
- **Expected max frequency**: <=10/task
- **KPI contribution**: phone_participation
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: censored = ended by disconnect, telemetry stop or match end (duration is a lower bound).

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `call_id` | int | yes | no |  |
| `caller` | slot (0-5) | yes | no |  |
| `callee` | slot (0-5) | yes | no |  |
| `duration_seconds` | float (seconds unless named otherwise) | yes | no |  |
| `end_reason` | enum | yes | no | @CallEnd (none, hung_up, declined, missed, phase_end, disconnected) |
| `censored` | bool | yes | no |  |

### phone_round_summary

- **Name / version**: `phone_round_summary` v1
- **Meaning**: Per task phone usage of observed players.
- **Trigger location**: `(removed v4) LineTelemetryObserver SummarizeRound`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: 1/task
- **KPI contribution**: phone_participation
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: participation = observed players with >= 1 successful connected call overlapping the task window / observed. rate omitted when observed = 0 (not_measured).

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `eligible` | int | yes | no |  |
| `observed` | int | yes | no |  |
| `participating` | int | yes | no |  |
| `participation_rate` | float (seconds unless named otherwise) | optional (omitted, never 0) | no |  |
| `attempts` | int | yes | no |  |
| `connected` | int | yes | no |  |
| `distinct_pairs` | int | yes | no |  |
| `connected_seconds_union` | float (seconds unless named otherwise) | yes | no |  |

### secret_info_presented

- **Name / version**: `secret_info_presented` v1
- **Meaning**: Private facts were dealt to a player.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnPhaseChanged (RoundPlay) via BuildView`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=6/task
- **KPI contribution**: information_asymmetry
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `player` | slot (0-5) | yes | no |  |
| `role` | int | yes | no |  |
| `facts` | int | yes | no |  |

### choice_ui_opened

- **Name / version**: `choice_ui_opened` v1
- **Meaning**: The player opened the task controls.
- **Trigger location**: `Scripts/Telemetry/ClientTelemetry.cs ClientTelemetry.ChoiceUiOpened via UI: Scripts/UI/Desk/DeskTaskApp.cs (task app opened)`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: 1/task
- **KPI contribution**: accessibility, pacing
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `task_id` | enum | yes | no | @Task (client_bonus, petty_cash, weekend_shift) |
| `seconds_into_phase` | float (seconds unless named otherwise) | yes | no |  |

### choice_submitted

- **Name / version**: `choice_submitted` v1
- **Meaning**: A player set task controls.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnHostChoice`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=12/task
- **KPI contribution**: information_asymmetry, communication
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: category per docs/CONTRACTS_V4.md section 8. No lie or intent is inferred.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `player` | slot (0-5) | yes | no |  |
| `field` | enum | yes | no | target, a, b, multiple |
| `category` | int | yes | no |  |
| `latency_seconds` | float (seconds unless named otherwise) | yes | no |  |
| `after_contact` | bool | yes | no |  |
| `contact_channel` | enum | yes | no | none, voice, text, both |
| `sabotage_option` | bool | yes | no |  |

### choice_changed

- **Name / version**: `choice_changed` v1
- **Meaning**: A player changed an earlier submission.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnHostChoice`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=6/task
- **KPI contribution**: information_asymmetry
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `player` | slot (0-5) | yes | no |  |
| `field` | enum | yes | no | target, a, b, multiple |
| `category` | int | yes | no |  |
| `latency_seconds` | float (seconds unless named otherwise) | yes | no |  |
| `after_contact` | bool | yes | no |  |
| `contact_channel` | enum | yes | no | none, voice, text, both |
| `sabotage_option` | bool | yes | no |  |
| `previous_category` | int | optional (omitted, never 0) | no |  |

### choice_timed_out

- **Name / version**: `choice_timed_out` v1
- **Meaning**: A submission was missing at resolution.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnHostMissedTask`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=6/task
- **KPI contribution**: accessibility, blame_tension
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `player` | slot (0-5) | yes | no |  |
| `reason` | enum | yes | no | missed, blocked, absent |

### choice_outcome_resolved

- **Name / version**: `choice_outcome_resolved` v1
- **Meaning**: Outcome of one player's task contribution (quality, productive income, cooperation).
- **Trigger location**: `(removed v4) LineTelemetryObserver OnHostOutcome`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=6/task
- **KPI contribution**: information_asymmetry
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: From engine HostOutcome (49).

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `player` | slot (0-5) | yes | no |  |
| `quality` | int | yes | no |  |
| `income` | int | yes | no |  |
| `cooperation_credit` | bool | yes | no |  |
| `cooperation_opportunity` | bool | yes | no |  |

### round_player_summary

- **Name / version**: `round_player_summary` v1
- **Meaning**: Per player per task interaction summary.
- **Trigger location**: `(removed v4) LineTelemetryObserver SummarizeRound`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=6/task
- **KPI contribution**: communication, accessibility, phone_participation
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: longest_gap = longest stretch of the task window without a connected call, message or submission. post_submit_wait omitted when never submitted; first_contact omitted when no contact.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `player` | slot (0-5) | yes | no |  |
| `connected_seconds` | float (seconds unless named otherwise) | yes | no |  |
| `calls_attempted` | int | yes | no |  |
| `calls_connected` | int | yes | no |  |
| `unique_partners` | int | yes | no |  |
| `texts_sent` | int | yes | no |  |
| `emails_sent` | int | yes | no |  |
| `first_contact_latency_seconds` | float (seconds unless named otherwise) | optional (omitted, never 0) | no |  |
| `submitted` | bool | yes | no |  |
| `revisions` | int | yes | no |  |
| `longest_gap_seconds` | float (seconds unless named otherwise) | yes | no |  |
| `post_submit_wait_seconds` | float (seconds unless named otherwise) | optional (omitted, never 0) | no |  |
| `busy_failures` | int | yes | no |  |
| `unanswered_attempts` | int | yes | no |  |
| `comm_mode` | enum | yes | no | none, voice, text, mixed |
| `blocked_seconds` | float (seconds unless named otherwise) | yes | no |  |

### meeting_started

- **Name / version**: `meeting_started` v1
- **Meaning**: A meeting began (MeetingDossier).
- **Trigger location**: `(removed v4) LineTelemetryObserver OnPhaseChanged (MeetingDossier)`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: <=4/match
- **KPI contribution**: meeting_engagement
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `is_final` | bool | yes | no |  |
| `recordings_scheduled` | int | yes | no |  |
| `complaints` | int | yes | no |  |
| `incident_effects` | int | yes | no |  |

### recording_playback_started

- **Name / version**: `recording_playback_started` v1
- **Meaning**: A recorded call snippet started to play in a meeting.
- **Trigger location**: `(removed v4) LineTelemetryObserver RecordingStarted (via HostTelemetryBridge.RecordingStarted)`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `all_participants`
- **Routing**: local
- **Expected max frequency**: <=2/meeting
- **KPI contribution**: recordings
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: Needs the voice bridge call site. Audio, paths and transcripts are never recorded.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `pick_index` | int | yes | no |  |
| `candidates` | int | yes | no |  |
| `chosen_rank` | int | yes | no |  |
| `speech_ms` | int | yes | no |  |
| `snippet_ms` | int | yes | no |  |
| `call_length_ms` | int | yes | no |  |
| `caller` | slot (0-5) | yes | no |  |
| `callee` | slot (0-5) | yes | no |  |
| `context_task` | enum | optional (omitted, never 0) | no | @Task (client_bonus, petty_cash, weekend_shift) |
| `same_group` | bool | yes | no |  |
| `prior_exposures` | int | yes | no |  |
| `selection` | enum | yes | no | legacy, context |

### recording_playback_finished

- **Name / version**: `recording_playback_finished` v1
- **Meaning**: The host tape ended or no candidate could play.
- **Trigger location**: `(removed v4) LineTelemetryObserver RecordingStarted/RecordingFinished`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: <=2/meeting
- **KPI contribution**: recordings
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `pick_index` | int | yes | no |  |
| `played` | bool | yes | no |  |
| `actual_duration_seconds` | float (seconds unless named otherwise) | optional (omitted, never 0) | no |  |
| `failure` | enum | yes | no | none, no_candidates, no_speech, disabled, voice_unavailable, stopped |

### meeting_discussion_started

- **Name / version**: `meeting_discussion_started` v1
- **Meaning**: Discussion phase began.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnPhaseChanged (MeetingDiscussion)`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: <=4/match
- **KPI contribution**: meeting_engagement
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Params**: none

### meeting_discussion_finished

- **Name / version**: `meeting_discussion_finished` v1
- **Meaning**: Discussion phase ended.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnPhaseChanged`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: <=4/match
- **KPI contribution**: meeting_engagement
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: chat_messages counts HostMessage meeting_chat (counts only).

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `duration_seconds` | float (seconds unless named otherwise) | yes | no |  |
| `chat_messages` | int | yes | no |  |
| `presentations` | int | yes | no |  |

### evidence_presented

- **Name / version**: `evidence_presented` v1
- **Meaning**: A record was presented in a meeting.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnEvidence (reads the presenter's view)`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=2/meeting
- **KPI contribution**: meeting_engagement, sabotage
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `presenter` | slot (0-5) | yes | no |  |
| `record_kind` | enum | yes | no | @RecordKind (call, transfer, task_entry, task_outcome, deal_payment, trick_log, spoofed_mail, badge_access, remedial, warning, audit_figure, report_draft, hr_file, item_transfer, offer_acceptance, trick_effect) |
| `record_source` | enum | yes | no | @RecordSource (phone_log, bank, task_system, it_log, badge_log, hr) |
| `exposed` | bool | yes | no |  |
| `repeat` | bool | yes | no |  |
| `record_age_seconds` | float (seconds unless named otherwise) | yes | no |  |

### meeting_vote_submitted

- **Name / version**: `meeting_vote_submitted` v1
- **Meaning**: A daily ballot was cast.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnVoteCast`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `voter`
- **Routing**: local
- **Expected max frequency**: <=6/meeting
- **KPI contribution**: vote_dynamics
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: Emitted when votes become public (MeetingVerdict) so the host log never shows ballots mid-vote; event time is the cast time.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `voter` | slot (0-5) | yes | no |  |
| `target` | slot or enum | yes | no | no_action, abstain, unconsented |
| `changed` | bool | yes | no |  |

### meeting_vote_resolved

- **Name / version**: `meeting_vote_resolved` v1
- **Meaning**: The daily ballot resolved.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnBallot`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local+cloud (cloud only for consent level 3; cloud payload = parameters marked cloud)
- **Expected max frequency**: <=4/match
- **KPI contribution**: vote_dynamics
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `outcome` | enum | yes | yes | warning_given, capped, tie, below_minimum, no_action_won, no_votes |
| `top_votes` | int | yes | no |  |
| `no_action_votes` | int | yes | no |  |
| `eligible` | int | yes | no |  |
| `voted` | int | yes | no |  |
| `target_was_blame_leader` | bool | optional (omitted, never 0) | no |  |

### warning_assigned

- **Name / version**: `warning_assigned` v1
- **Meaning**: A warning was given.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnWarning`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=4/match
- **KPI contribution**: blame_tension
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `target` | slot (0-5) | yes | no |  |
| `blame_before` | int | optional (omitted, never 0) | no |  |
| `blame_after` | int | yes | no |  |
| `warnings_after` | int | yes | no |  |

### meeting_finished

- **Name / version**: `meeting_finished` v1
- **Meaning**: The meeting (dossier to verdict) ended.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnPhaseChanged`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: <=4/match
- **KPI contribution**: meeting_engagement
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `duration_seconds` | float (seconds unless named otherwise) | yes | no |  |

### fault_points_changed

- **Name / version**: `fault_points_changed` v1
- **Meaning**: Blame changed.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnBlameChanged`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=20/match
- **KPI contribution**: blame_tension
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `player` | slot (0-5) | yes | no |  |
| `delta` | int | yes | no |  |
| `reason` | enum | yes | no | @BlameReason (vote, cash_caught, whistled, investigation_guilty, investigation_false, crisis) |
| `blame_after` | int | yes | no |  |

### final_started

- **Name / version**: `final_started` v1
- **Meaning**: The final began (defense or closure).
- **Trigger location**: `(removed v4) LineTelemetryObserver OnPhaseChanged`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: 1/match
- **KPI contribution**: final
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `roster` | int | yes | no |  |
| `company_closed` | bool | yes | no |  |

### final_candidate_selected

- **Name / version**: `final_candidate_selected` v1
- **Meaning**: Kept for analyzer compatibility with v3.
- **Trigger location**: -
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: 0
- **KPI contribution**: final
- **Availability**: not_applicable - no shortlist exists in the v4 ruleset
- **Params**: none

### defense_started

- **Name / version**: `defense_started` v1
- **Meaning**: A final defense turn began.
- **Trigger location**: `(removed v4) LineTelemetryObserver Tick (polls the public view during FinalDefense)`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=6/match
- **KPI contribution**: final
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `defender` | slot (0-5) | yes | no |  |
| `order_index` | int | yes | no |  |

### defense_finished

- **Name / version**: `defense_finished` v1
- **Meaning**: A defense turn ended.
- **Trigger location**: `(removed v4) LineTelemetryObserver Tick`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=6/match
- **KPI contribution**: final
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `defender` | slot (0-5) | yes | no |  |
| `order_index` | int | yes | no |  |
| `duration_seconds` | float (seconds unless named otherwise) | yes | no |  |

### final_vote_submitted

- **Name / version**: `final_vote_submitted` v1
- **Meaning**: A final ballot was cast.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnVoteCast`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `voter`
- **Routing**: local
- **Expected max frequency**: <=6/match
- **KPI contribution**: final, vote_dynamics
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: Emitted at FinalVerdict (see meeting_vote_submitted).

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `voter` | slot (0-5) | yes | no |  |
| `target` | slot or enum | yes | no | fire_nobody, abstain, unconsented |
| `changed` | bool | yes | no |  |

### final_vote_resolved

- **Name / version**: `final_vote_resolved` v1
- **Meaning**: The final ballot resolved.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnBallot`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local+cloud (cloud only for consent level 3; cloud payload = parameters marked cloud)
- **Expected max frequency**: 1/match
- **KPI contribution**: final
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `outcome` | enum | yes | yes | fired, no_majority, no_votes, company_closed |
| `roster` | int | yes | no |  |
| `majority_needed` | int | yes | no |  |
| `top_votes` | int | yes | no |  |
| `fire_nobody_votes` | int | yes | no |  |

### player_fired

- **Name / version**: `player_fired` v1
- **Meaning**: A player was fired.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnFired`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=1/match
- **KPI contribution**: final
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `player` | slot (0-5) | yes | no |  |
| `blame` | int | yes | no |  |
| `warnings` | int | yes | no |  |
| `was_blame_leader` | bool | yes | no |  |

### task_output_resolved

- **Name / version**: `task_output_resolved` v1
- **Meaning**: Task output resolved.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnTaskResolved + OnHostTaskOutcome`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: 1/task
- **KPI contribution**: productivity
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: sabotage_loss, groups and groups_full come from engine HostTaskOutcome (50); omitted if that event is missing.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `output` | int | yes | no |  |
| `max_output` | int | yes | no |  |
| `sabotage_loss` | int | optional (omitted, never 0) | no |  |
| `groups` | int | optional (omitted, never 0) | no |  |
| `groups_full` | int | optional (omitted, never 0) | no |  |

### productivity_changed

- **Name / version**: `productivity_changed` v1
- **Meaning**: Company productivity moved.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnProductivity`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: <=14/match
- **KPI contribution**: productivity
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: Not emitted while the engine reports -1 (measuring).

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `provisional_permille` | int | yes | no |  |
| `delta_permille` | int | yes | no |  |
| `change` | enum | yes | no | task, remedial |

### recovery_opportunities

- **Name / version**: `recovery_opportunities` v1
- **Meaning**: Remedial options available at a break.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnPhaseChanged (Break) via BuildView`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: 1/break
- **KPI contribution**: productivity, economy
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `players_with_options` | int | yes | no |  |
| `players` | int | yes | no |  |

### remedial_requested

- **Name / version**: `remedial_requested` v1
- **Meaning**: A remedial report was requested.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnRemedialRequested`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `all_participants`
- **Routing**: local
- **Expected max frequency**: <=2/match
- **KPI contribution**: economy, blame_tension
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `requester` | slot (0-5) | yes | no |  |
| `cosigner` | slot (0-5) | yes | no |  |

### remedial_resolved

- **Name / version**: `remedial_resolved` v1
- **Meaning**: A remedial request resolved.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnRemedialResolved`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `all_participants`
- **Routing**: local
- **Expected max frequency**: <=2/match
- **KPI contribution**: economy, blame_tension
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: output_added from RemedialResolved.C.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `requester` | slot (0-5) | yes | no |  |
| `cosigner` | slot (0-5) | yes | no |  |
| `accepted` | bool | yes | no |  |
| `output_added` | int | optional (omitted, never 0) | no |  |
| `blame_delta` | int | optional (omitted, never 0) | no |  |

### income_credited

- **Name / version**: `income_credited` v1
- **Meaning**: Money was credited.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnHostIncome`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=30/match
- **KPI contribution**: economy
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `player` | slot (0-5) | yes | no |  |
| `amount` | int | yes | no |  |
| `raw_amount` | int | yes | no |  |
| `reason` | enum | yes | no | @MoneyReason (task_income, deal_income, transfer_in, transfer_out, purchase, remedial, favourite) |
| `block_total` | int | yes | no |  |

### money_transferred

- **Name / version**: `money_transferred` v1
- **Meaning**: A transfer happened.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnTransfer`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `all_participants`
- **Routing**: local
- **Expected max frequency**: <=20/match
- **KPI contribution**: economy
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `from` | slot (0-5) | yes | no |  |
| `to` | slot (0-5) | yes | no |  |
| `amount` | int | yes | no |  |
| `phase` | enum | yes | no | @Phase (intro, task_play, task_reveal, break, meeting_dossier, meeting_discussion, meeting_vote, meeting_verdict, crisis_calls, crisis_vote, crisis_verdict, final_discussion, final_defense, final_vote, final_verdict, results, abandoned) |

### evidence_purchased

- **Name / version**: `evidence_purchased` v1
- **Meaning**: An evidence offer was bought.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnPurchase`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=3/match
- **KPI contribution**: economy
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `buyer` | slot (0-5) | yes | no |  |
| `offer_kind` | enum | yes | no | @OfferKind (bank_ledger, task_audit, it_log, badge_log) |
| `price` | int | yes | no |  |
| `purchase_index` | int | yes | no |  |

### incident_created

- **Name / version**: `incident_created` v1
- **Meaning**: A deniable act was created.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnHostIncident`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=10/match
- **KPI contribution**: sabotage
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `actor` | slot (0-5) | yes | no |  |
| `kind` | enum | yes | no | @IncidentKind (invoice_padding, cooked_report, archive_snoop, accepted_competitor_offer, accepted_kickback, vendor_kickback, trick_coffee, trick_phone_cut, trick_internet, trick_fake_email, trick_wiretap) |
| `victim` | slot (0-5) | optional (omitted, never 0) | no |  |

### sabotage_verified

- **Name / version**: `sabotage_verified` v1
- **Meaning**: An incident was proven in a meeting.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnIncidentExposed`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=10/match
- **KPI contribution**: sabotage
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `actor` | slot (0-5) | yes | no |  |
| `kind` | enum | yes | no | @IncidentKind (invoice_padding, cooked_report, archive_snoop, accepted_competitor_offer, accepted_kickback, vendor_kickback, trick_coffee, trick_phone_cut, trick_internet, trick_fake_email, trick_wiretap) |
| `exposed_by` | slot (0-5) | optional (omitted, never 0) | no |  |
| `seconds_since_incident` | float (seconds unless named otherwise) | optional (omitted, never 0) | no |  |

### objective_assigned

- **Name / version**: `objective_assigned` v1
- **Meaning**: A secret objective was dealt.
- **Trigger location**: `(removed v4) LineTelemetryObserver Start via BuildView`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=6/match
- **KPI contribution**: information_asymmetry
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `player` | slot (0-5) | yes | no |  |
| `kind` | enum | yes | no | @ObjectiveKind (saver, broker, patron, team_player, clean_record, rival, guardian, archivist, presenter) |

### objective_completed

- **Name / version**: `objective_completed` v1
- **Meaning**: Objective evaluated.
- **Trigger location**: `(removed v4) LineTelemetryObserver EmitResults (engine.Result at WeekResults; the FinalVerdict HostObjective event is not written early)`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=6/match
- **KPI contribution**: career
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `player` | slot (0-5) | yes | no |  |
| `kind` | enum | yes | no | @ObjectiveKind (saver, broker, patron, team_player, clean_record, rival, guardian, archivist, presenter) |
| `completed` | bool | yes | no |  |

### career_breakdown

- **Name / version**: `career_breakdown` v1
- **Meaning**: Career points at results.
- **Trigger location**: `(removed v4) LineTelemetryObserver EmitResults (engine.Result)`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=6/match
- **KPI contribution**: career
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: quality omitted when the engine reports -1.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `player` | slot (0-5) | yes | no |  |
| `not_fired` | int | yes | no |  |
| `company_target` | int | yes | no |  |
| `objective` | int | yes | no |  |
| `eotm` | int | yes | no |  |
| `sabotage_penalty` | int | yes | no |  |
| `total` | int | yes | no |  |
| `rank` | int | yes | no |  |
| `money_end` | int | yes | no |  |
| `quality` | int | optional (omitted, never 0) | no |  |

### company_result

- **Name / version**: `company_result` v1
- **Meaning**: Final company outcome.
- **Trigger location**: `(removed v4) LineTelemetryObserver EmitResults`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local+cloud (cloud only for consent level 3; cloud payload = parameters marked cloud)
- **Expected max frequency**: 1/match
- **KPI contribution**: productivity
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `final_permille` | int | yes | yes |  |
| `target_percent` | int | yes | yes |  |
| `closed` | bool | yes | yes |  |
| `exposed_incidents` | int | yes | no |  |
| `incidents` | int | yes | no |  |

### company_failed

- **Name / version**: `company_failed` v1
- **Meaning**: The company closed.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnCompanyClosed`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: <=1/match
- **KPI contribution**: productivity
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `final_permille` | int | yes | no |  |

### message_sent

- **Name / version**: `message_sent` v1
- **Meaning**: A message was sent (length bucket only, never text).
- **Trigger location**: `(removed v4) LineTelemetryObserver OnHostMessage`
- **Authority**: host
- **Privacy / consent**: private_until_match_end (held in host memory, written at results/abandon); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=100/match
- **KPI contribution**: communication, accessibility
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `sender` | slot (0-5) | yes | no |  |
| `channel` | enum | yes | no | text, email, meeting_chat, complaint, fake_email |
| `scope` | enum | yes | no | one, all |
| `recipient` | slot (0-5) | optional (omitted, never 0) | no |  |
| `length_bucket` | enum | yes | no | short, medium, long |
| `is_reply` | bool | yes | no |  |
| `template` | bool | yes | no |  |
| `preset` | bool | yes | no |  |

### message_rejected

- **Name / version**: `message_rejected` v1
- **Meaning**: The host refused a message.
- **Trigger location**: `Scripts/Telemetry/ClientTelemetry.cs ClientTelemetry.OnResult`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: rare
- **KPI contribution**: accessibility
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `channel` | enum | yes | no | text, email, meeting_chat, complaint, fake_email |
| `reason` | enum | yes | no | too_long, empty, rate_limited, limit_reached, wrong_phase |

### message_thread_opened

- **Name / version**: `message_thread_opened` v1
- **Meaning**: The player opened a thread.
- **Trigger location**: `Scripts/Telemetry/ClientTelemetry.cs ClientTelemetry.MessageThreadOpened via UI: Scripts/UI/Desk/DeskComms.cs`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=50/match
- **KPI contribution**: communication
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `channel` | enum | yes | no | text, email, meeting_chat, complaint, fake_email |
| `unread` | int | yes | no |  |

### ui_action_rejected

- **Name / version**: `ui_action_rejected` v1
- **Meaning**: An action was refused by the host.
- **Trigger location**: `Scripts/Telemetry/ClientTelemetry.cs ClientTelemetry.OnResult`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=50/match
- **KPI contribution**: accessibility
- **Availability**: implemented
- **Notes**: Message verbs go to message_rejected instead.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `verb` | enum | yes | no | @Verb (call, answer, decline, hang_up, submit_task, vote, send_text, send_meeting_chat, transfer, whistle, investigate, record, leak, host_continue) |
| `failure` | enum | yes | no | @Failure (wrong_phase, stale_week, duplicate, invalid_target, self, busy, already_used, not_available, already_in_call, not_in_call, not_ringing, bad_param, absent, host_only, limit_reached, too_long, empty_text, rate_limited, insufficient_funds, not_owned, not_eligible, already_resolved, fired, desk_sealed) |

### tutorial_step_viewed

- **Name / version**: `tutorial_step_viewed` v1
- **Meaning**: A tutorial step was shown.
- **Trigger location**: `Scripts/Telemetry/ClientTelemetry.cs ClientTelemetry.TutorialStep via UI: Scripts/UI/Desk/DeskTutorial.cs`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=60/match
- **KPI contribution**: onboarding
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `task_id` | enum | yes | no | @Task (client_bonus, petty_cash, weekend_shift) |
| `step` | int | yes | no |  |
| `action` | enum | yes | no | auto, next, back, reopen |

### tutorial_closed

- **Name / version**: `tutorial_closed` v1
- **Meaning**: The tutorial card closed.
- **Trigger location**: `Scripts/Telemetry/ClientTelemetry.cs ClientTelemetry.TutorialClosed via UI: Scripts/UI/Desk/DeskTutorial.cs`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local+cloud (cloud only for consent level 3; cloud payload = parameters marked cloud)
- **Expected max frequency**: <=12/match
- **KPI contribution**: onboarding
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `task_id` | enum | yes | yes | @Task (client_bonus, petty_cash, weekend_shift) |
| `steps_seen` | int | yes | yes |  |
| `seconds_open` | float (seconds unless named otherwise) | yes | no |  |
| `completed` | bool | yes | yes |  |

### onboarding_intro_completed

- **Name / version**: `onboarding_intro_completed` v1
- **Meaning**: The intro finished.
- **Trigger location**: `Scripts/Telemetry/ClientTelemetry.cs ClientTelemetry.OnboardingIntroCompleted via UI: Scripts/UI/ShellOverlays.cs (first-launch intro)`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: 1/match
- **KPI contribution**: onboarding
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `cards_seen` | int | yes | no |  |
| `skipped` | bool | yes | no |  |

### settings_changed

- **Name / version**: `settings_changed` v1
- **Meaning**: A setting changed (coarse value only).
- **Trigger location**: `Scripts/Telemetry/ClientTelemetry.cs ClientTelemetry.SettingChanged via UI: Scripts/UI/SettingsScreen.cs`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=30/session
- **KPI contribution**: accessibility
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `setting` | enum | yes | no | music_volume, music_mute, sfx_volume, voice_volume, mic_gain, language, fullscreen, quality, sensitivity, other |
| `value` | enum | optional (omitted, never 0) | no | off, on, low, medium, high, tr, en, other |
| `value_percent` | int | optional (omitted, never 0) | no |  |

### player_disconnected

- **Name / version**: `player_disconnected` v1
- **Meaning**: A player dropped mid-match.
- **Trigger location**: `Core/Telemetry/HostTelemetryCoordinator.cs HostTelemetryCoordinator.PlayerConnection`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=5/match
- **KPI contribution**: connection
- **Availability**: implemented
- **Notes**: Needs the HostLineController call site.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `player` | slot (0-5) | yes | no |  |
| `phase` | enum | yes | no | @Phase (intro, task_play, task_reveal, break, meeting_dossier, meeting_discussion, meeting_vote, meeting_verdict, crisis_calls, crisis_vote, crisis_verdict, final_discussion, final_defense, final_vote, final_verdict, results, abandoned) |

### player_reconnected

- **Name / version**: `player_reconnected` v1
- **Meaning**: A player came back.
- **Trigger location**: `Core/Telemetry/HostTelemetryCoordinator.cs HostTelemetryCoordinator.PlayerConnection`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=5/match
- **KPI contribution**: connection
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `player` | slot (0-5) | yes | no |  |
| `phase` | enum | yes | no | @Phase (intro, task_play, task_reveal, break, meeting_dossier, meeting_discussion, meeting_vote, meeting_verdict, crisis_calls, crisis_vote, crisis_verdict, final_discussion, final_defense, final_vote, final_verdict, results, abandoned) |
| `downtime_seconds` | float (seconds unless named otherwise) | optional (omitted, never 0) | no |  |

### reconnect_attempted

- **Name / version**: `reconnect_attempted` v1
- **Meaning**: The client entered the reconnect loop.
- **Trigger location**: `Core/Telemetry/ClientConnectionTelemetry.cs ClientConnectionTelemetry.OnState (Reconnecting)`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=3/interruption
- **KPI contribution**: connection
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `attempt` | int | yes | no |  |
| `seconds` | float (seconds unless named otherwise) | yes | no |  |

### reconnect_succeeded

- **Name / version**: `reconnect_succeeded` v1
- **Meaning**: The reconnect worked.
- **Trigger location**: `Core/Telemetry/ClientConnectionTelemetry.cs ClientConnectionTelemetry.OnState`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=3/interruption
- **KPI contribution**: connection
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `attempt` | int | yes | no |  |
| `seconds` | float (seconds unless named otherwise) | yes | no |  |

### reconnect_failed

- **Name / version**: `reconnect_failed` v1
- **Meaning**: The reconnect loop gave up.
- **Trigger location**: `Core/Telemetry/ClientConnectionTelemetry.cs ClientConnectionTelemetry.Tick (terminal)`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=3/interruption
- **KPI contribution**: connection
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `attempt` | int | yes | no |  |
| `seconds` | float (seconds unless named otherwise) | yes | no |  |

### host_departed

- **Name / version**: `host_departed` v1
- **Meaning**: The host went away.
- **Trigger location**: `Core/Telemetry/ClientConnectionTelemetry.cs ClientConnectionTelemetry.OnState`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=1/session
- **KPI contribution**: connection
- **Availability**: implemented
- **Notes**: graceful when the match was already over.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `phase` | enum | yes | no | @PhaseLobby (intro, task_play, task_reveal, break, meeting_dossier, meeting_discussion, meeting_vote, meeting_verdict, crisis_calls, crisis_vote, crisis_verdict, final_discussion, final_defense, final_vote, final_verdict, results, abandoned, lobby, none) |
| `graceful` | bool | yes | no |  |

### voice_quality_summary

- **Name / version**: `voice_quality_summary` v1
- **Meaning**: Voice link quality.
- **Trigger location**: `Scripts/Telemetry/ClientTelemetry.cs  (not emitted)`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: 1/60s
- **KPI contribution**: connection, accessibility
- **Availability**: pending_integration - VoiceSystem exposes stats only as a log string (VoiceStatsReporter.Build); a structured snapshot API is required

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `loss_pct` | float (seconds unless named otherwise) | yes | no |  |
| `jitter_ms` | float (seconds unless named otherwise) | yes | no |  |
| `underruns` | int | yes | no |  |

### stuck_phase

- **Name / version**: `stuck_phase` v1
- **Meaning**: A phase overran its end time.
- **Trigger location**: `(removed v4) LineTelemetryObserver Tick`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: rare
- **KPI contribution**: telemetry_health
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)
- **Notes**: Threshold 5 s past engine.PhaseEndTime, once per phase.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `phase` | enum | yes | no | @Phase (intro, task_play, task_reveal, break, meeting_dossier, meeting_discussion, meeting_vote, meeting_verdict, crisis_calls, crisis_vote, crisis_verdict, final_discussion, final_defense, final_vote, final_verdict, results, abandoned) |
| `seconds_over` | float (seconds unless named otherwise) | yes | no |  |

### unexpected_transition

- **Name / version**: `unexpected_transition` v1
- **Meaning**: A phase change outside the documented flow.
- **Trigger location**: `(removed v4) LineTelemetryObserver OnPhaseChanged`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `host_aggregate`
- **Routing**: local
- **Expected max frequency**: rare
- **KPI contribution**: telemetry_health
- **Availability**: pending_integration - v5 gameplay observer not implemented yet (the v4 LineTelemetryObserver was removed with the v4 rules in v5 Phase 1)

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `from` | enum | yes | no | @PhaseLobby (intro, task_play, task_reveal, break, meeting_dossier, meeting_discussion, meeting_vote, meeting_verdict, crisis_calls, crisis_vote, crisis_verdict, final_discussion, final_defense, final_vote, final_verdict, results, abandoned, lobby, none) |
| `to` | enum | yes | no | @PhaseLobby (intro, task_play, task_reveal, break, meeting_dossier, meeting_discussion, meeting_vote, meeting_verdict, crisis_calls, crisis_vote, crisis_verdict, final_discussion, final_defense, final_vote, final_verdict, results, abandoned, lobby, none) |

### technical_failure

- **Name / version**: `technical_failure` v1
- **Meaning**: A caught exception (type name only).
- **Trigger location**: `ct/hc:TechnicalFailure`
- **Authority**: both
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local+cloud (cloud only for consent level 3; cloud payload = parameters marked cloud)
- **Expected max frequency**: rare
- **KPI contribution**: telemetry_health
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `category` | enum | yes | yes | engine_exception, rpc_decode, voice_exception, sink_error, ui_exception |
| `type` | id (TypeName) | yes | no |  |

### telemetry_queue_dropped

- **Name / version**: `telemetry_queue_dropped` v1
- **Meaning**: Events were dropped (counted, never silent).
- **Trigger location**: `Core/Telemetry/GameTelemetryService.cs GameTelemetryService.Flush`
- **Authority**: both
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: rare
- **KPI contribution**: telemetry_health
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `dropped` | int | yes | no |  |
| `reason` | enum | yes | no | overflow, consent_withdrawn, shutdown |

### telemetry_sink_error

- **Name / version**: `telemetry_sink_error` v1
- **Meaning**: A sink threw; reported once per fault.
- **Trigger location**: `Core/Telemetry/GameTelemetryService.cs GameTelemetryService.Flush`
- **Authority**: both
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: rare
- **KPI contribution**: telemetry_health
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `sink` | enum | yes | no | local_jsonl, debug, unity_analytics, other |
| `category` | enum | yes | no | io, exception, full, unknown |

### moment_marked

- **Name / version**: `moment_marked` v1
- **Meaning**: The player marked a moment.
- **Trigger location**: `Scripts/Telemetry/ClientTelemetry.cs ClientTelemetry.MomentMarked via UI: Scripts/UI/ShellOverlays.cs (F8 / flag)`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=20/match
- **KPI contribution**: retention, tension
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `category` | enum | yes | no | funny, betrayal, accusation, surprise, reveal, other |
| `source` | enum | yes | no | player |

### music_cue_changed

- **Name / version**: `music_cue_changed` v1
- **Meaning**: Music cue changed.
- **Trigger location**: `Scripts/Telemetry/ClientTelemetry.cs ClientTelemetry.MusicCueChanged via Scripts/Audio/MusicDirector.cs (cue changes only)`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: <=30/match
- **KPI contribution**: accessibility
- **Availability**: implemented

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `cue` | enum | yes | no | menu_lobby, office_work, deadline, meeting_intro, meeting_discussion, final_vote, results_success, results_collapse, none, other |
| `ducked` | bool | yes | no |  |

### rematch_prompt_shown

- **Name / version**: `rematch_prompt_shown` v1
- **Meaning**: The play again prompt appeared.
- **Trigger location**: `Scripts/Telemetry/ClientTelemetry.cs ClientTelemetry.RematchPromptShown via UI: Scripts/UI/Meeting/WeekResultsView.cs (results with >= 3 players)`
- **Authority**: client
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: 1/results
- **KPI contribution**: retention
- **Availability**: implemented
- **Params**: none

### player_slot_map

- **Name / version**: `player_slot_map` v1
- **Meaning**: Seat to anonymous id mapping of consenting players.
- **Trigger location**: `Core/Telemetry/HostTelemetryCoordinator.cs HostTelemetryCoordinator.MatchStarted`
- **Authority**: host
- **Privacy / consent**: public (written immediately); consent rule `self`
- **Routing**: local
- **Expected max frequency**: 1/player/match
- **KPI contribution**: privacy
- **Availability**: implemented
- **Notes**: Only consenting players are written.

| Param | Type | Required | Cloud | Values |
|---|---|---|---|---|
| `slot` | slot (0-5) | yes | no |  |
| `anonymous_player_id` | id (PlayerId) | optional (omitted, never 0) | no |  |
| `is_bot` | bool | yes | no |  |
