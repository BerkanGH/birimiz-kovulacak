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
    /// v5 Phase 1: the v4 gameplay observer (LineTelemetryObserver, "obs:" rows) was removed with the v4 rules; v5 gameplay
    /// events return with the v5 observer. Lifecycle, connection, client and health events below are unchanged.
    /// Trigger prefixes: hc: = Core/Telemetry/HostTelemetryCoordinator.cs, cc: = Core/Telemetry/ClientConnectionTelemetry.cs, ct: = Scripts/Telemetry/ClientTelemetry.cs, svc: = Core/Telemetry/GameTelemetryService.cs.
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
            "lobby_created|H|P|H|L|b:recordings_enabled|I|1/hosting session|funnel|The host opened a lobby.|hc:HostTelemetryCoordinator.LobbyCreated (via HostTelemetryBridge.LobbyCreated)|Needs the HostLineController call site.",
            "lobby_join_attempted|C|P|S|L|e:kind(manual|rejoin|auto)|I|1/attempt|funnel,connection|A join attempt started.|cc:ClientConnectionTelemetry.OnState (Connecting)|kind is manual unless ClientTelemetry.NoteJoinKind said otherwise (auto = -autojoin launch arg).",
            "lobby_joined|C|P|S|L|b:reconnect,f:wait_seconds|I|1/join|funnel,connection|The client is connected to a lobby.|cc:ClientConnectionTelemetry.OnState (Connected)|",
            "lobby_join_failed|C|P|S|LC|*e:reason(reject.version|reject.full|reject.in_progress|net.connect_failed|net.host_lost|net.reconnect_failed)|I|1/failed attempt|funnel,connection|A join or rejoin ended in failure.|cc:ClientConnectionTelemetry.OnState (terminal Failed)|Other reject.* reasons are reported as net.connect_failed.",
            "lobby_left|C|P|S|L|e:reason(user|host_closed|error),f:seconds_in_lobby|I|1/session|retention|The client left the lobby.|cc:ClientConnectionTelemetry.OnState|",
            "player_ready_changed|H|P|S|L|s:slot,b:ready,f:seconds_since_join|I|<=10/lobby|funnel|A seat changed its ready flag.|hc:HostTelemetryCoordinator.PlayerReadyChanged|Clock = telemetry monotonic clock (lobby has no engine time).",
            "match_start_attempted|H|P|H|L|i:humans,i:bots,i:ready|I|1/start press|funnel|The host pressed start.|hc:HostTelemetryCoordinator.MatchStartAttempted|",
            "match_started|H|P|H|LC|*i:humans,*i:bots,b:recordings_enabled,f:lobby_fill_seconds?,f:ready_seconds?|I|1/match|funnel|The engine was created.|hc:HostTelemetryCoordinator.MatchStarted|lobby_fill_seconds = lobby created to last seat joined; ready_seconds = all ready to start (omitted when not observed).",
            "match_start_failed|H|P|H|L|e:reason(lobby.need_players|lobby.need_ready|lobby.pool_too_small|engine_error)|I|rare|funnel|The match could not start.|hc:HostTelemetryCoordinator.MatchStartFailed|Unknown reason keys map to engine_error.",
            "rematch_requested|C|P|S|L|f:seconds_after_results|I|1/results|retention|The player pressed play again.|ct:ClientTelemetry.RematchRequested via UI: Scripts/UI/Meeting/WeekResultsView.cs (Play again)|",
            "rematch_started|H|P|H|LC|id:previous_match_id(match),*i:returning_humans,*b:auto,*i:requests|I|1/rematch|retention|A new match began from the same lobby.|hc:HostTelemetryCoordinator.RematchStarted|Needs the HostLineController call site.",

            // ---- 5.2 progression

            // ---- 5.3 phone

            // ---- 5.4 information / choices
            "choice_ui_opened|C|P|S|L|e:task_id(@Task),f:seconds_into_phase|I|1/task|accessibility,pacing|The player opened the task controls.|ct:ClientTelemetry.ChoiceUiOpened via UI: Scripts/UI/Desk/DeskTaskApp.cs (task app opened)|",

            // ---- 5.5 meetings and recordings

            // ---- 5.6 blame / final

            // ---- 5.7 hybrid economy and outcomes

            // ---- 5.8 communication accessibility and diagnostics
            "message_rejected|C|P|S|L|e:channel(text|email|meeting_chat|complaint|fake_email),e:reason(too_long|empty|rate_limited|limit_reached|wrong_phase)|I|rare|accessibility|The host refused a message.|ct:ClientTelemetry.OnResult|",
            "message_thread_opened|C|P|S|L|e:channel(text|email|meeting_chat|complaint|fake_email),i:unread|I|<=50/match|communication|The player opened a thread.|ct:ClientTelemetry.MessageThreadOpened via UI: Scripts/UI/Desk/DeskComms.cs|",
            "ui_action_rejected|C|P|S|L|e:verb(@Verb),e:failure(@Failure)|I|<=50/match|accessibility|An action was refused by the host.|ct:ClientTelemetry.OnResult|Message verbs go to message_rejected instead.",
            "tutorial_step_viewed|C|P|S|L|e:task_id(@Task),i:step,e:action(auto|next|back|reopen)|I|<=60/match|onboarding|A tutorial step was shown.|ct:ClientTelemetry.TutorialStep via UI: Scripts/UI/Desk/DeskTutorial.cs|",
            "tutorial_closed|C|P|S|LC|*e:task_id(@Task),*i:steps_seen,f:seconds_open,*b:completed|I|<=12/match|onboarding|The tutorial card closed.|ct:ClientTelemetry.TutorialClosed via UI: Scripts/UI/Desk/DeskTutorial.cs|",
            "onboarding_intro_completed|C|P|S|L|i:cards_seen,b:skipped|I|1/match|onboarding|The intro finished.|ct:ClientTelemetry.OnboardingIntroCompleted via UI: Scripts/UI/ShellOverlays.cs (first-launch intro)|",
            "settings_changed|C|P|S|L|e:setting(music_volume|music_mute|sfx_volume|voice_volume|mic_gain|language|fullscreen|quality|sensitivity|other),e:value?(off|on|low|medium|high|tr|en|other),i:value_percent?|I|<=30/session|accessibility|A setting changed (coarse value only).|ct:ClientTelemetry.SettingChanged via UI: Scripts/UI/SettingsScreen.cs|",
            "player_disconnected|H|P|S|L|s:player,e:phase(@Phase)|I|<=5/match|connection|A player dropped mid-match.|hc:HostTelemetryCoordinator.PlayerConnection|Needs the HostLineController call site.",
            "player_reconnected|H|P|S|L|s:player,e:phase(@Phase),f:downtime_seconds?|I|<=5/match|connection|A player came back.|hc:HostTelemetryCoordinator.PlayerConnection|",
            "reconnect_attempted|C|P|S|L|i:attempt,f:seconds|I|<=3/interruption|connection|The client entered the reconnect loop.|cc:ClientConnectionTelemetry.OnState (Reconnecting)|",
            "reconnect_succeeded|C|P|S|L|i:attempt,f:seconds|I|<=3/interruption|connection|The reconnect worked.|cc:ClientConnectionTelemetry.OnState|",
            "reconnect_failed|C|P|S|L|i:attempt,f:seconds|I|<=3/interruption|connection|The reconnect loop gave up.|cc:ClientConnectionTelemetry.Tick (terminal)|",
            "host_departed|C|P|S|L|e:phase(@PhaseLobby),b:graceful|I|<=1/session|connection|The host went away.|cc:ClientConnectionTelemetry.OnState|graceful when the match was already over.",
            "voice_quality_summary|C|P|S|L|f:loss_pct,f:jitter_ms,i:underruns|P:VoiceSystem exposes stats only as a log string (VoiceStatsReporter.Build); a structured snapshot API is required|1/60s|connection,accessibility|Voice link quality.|ct: (not emitted)|",
            "technical_failure|B|P|S|LC|*e:category(engine_exception|rpc_decode|voice_exception|sink_error|ui_exception),id:type(type)|I|rare|telemetry_health|A caught exception (type name only).|ct/hc:TechnicalFailure|",
            "telemetry_queue_dropped|B|P|S|L|i:dropped,e:reason(overflow|consent_withdrawn|shutdown)|I|rare|telemetry_health|Events were dropped (counted, never silent).|svc:GameTelemetryService.Flush|",
            "telemetry_sink_error|B|P|S|L|e:sink(local_jsonl|debug|unity_analytics|other),e:category(io|exception|full|unknown)|I|rare|telemetry_health|A sink threw; reported once per fault.|svc:GameTelemetryService.Flush|",
            "moment_marked|C|P|S|L|e:category(funny|betrayal|accusation|surprise|reveal|other),e:source(player)|I|<=20/match|retention,tension|The player marked a moment.|ct:ClientTelemetry.MomentMarked via UI: Scripts/UI/ShellOverlays.cs (F8 / flag)|",
            "music_cue_changed|C|P|S|L|e:cue(menu_lobby|office_work|deadline|meeting_intro|meeting_discussion|final_vote|results_success|results_collapse|none|other),b:ducked|I|<=30/match|accessibility|Music cue changed.|ct:ClientTelemetry.MusicCueChanged via Scripts/Audio/MusicDirector.cs (cue changes only)|",
            "rematch_prompt_shown|C|P|S|L||I|1/results|retention|The play again prompt appeared.|ct:ClientTelemetry.RematchPromptShown via UI: Scripts/UI/Meeting/WeekResultsView.cs (results with >= 3 players)|",

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
                { "BlameReason", TelemetryNames.EnumNames<BlameReason>(true) },
                { "PrimReason", TelemetryNames.EnumNames<PrimReason>(true) },
                { "CallEnd", TelemetryNames.EnumNames<CallEndReason>(false) },
                { "Verb", TelemetryNames.EnumNames<LineVerb>(true) },
                { "Failure", TelemetryNames.EnumNames<LineFailure>(true) },
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
