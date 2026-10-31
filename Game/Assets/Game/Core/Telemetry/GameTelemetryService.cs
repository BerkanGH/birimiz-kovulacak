using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace BirimizKovulacak.Telemetry
{
    /// <summary>
    /// Bounded queue + consent gate + dedupe + sink fan-out. Thread model: events are built and submitted on the main
    /// thread (context is main-thread only); <see cref="Flush"/> may run on a background thread, it only touches events
    /// that were detached under the queue lock. A disabled service costs one volatile read per call.
    /// </summary>
    public sealed class GameTelemetryService : IGameTelemetry, IDisposable
    {
        const int MaxSeats = 8;
        const int SeenCapacity = 32768;

        sealed class Participant { public bool Present; public int Consent; public string AnonId = ""; public bool IsBot; }

        readonly object _gate = new object();
        readonly object _flushGate = new object();
        readonly TelemetryConfiguration _cfg;
        readonly Func<double> _clock;
        readonly Func<DateTime> _utcNow;
        readonly Func<ulong> _random64;
        readonly List<ITelemetrySink> _sinks = new List<ITelemetrySink>();
        readonly Dictionary<ITelemetrySink, bool> _faulted = new Dictionary<ITelemetrySink, bool>();
        readonly LinkedList<TelemetryEvent> _queue = new LinkedList<TelemetryEvent>();
        readonly List<TelemetryEvent> _private = new List<TelemetryEvent>();
        readonly HashSet<ulong> _seen = new HashSet<ulong>();
        readonly Queue<ulong> _seenOrder = new Queue<ulong>();
        readonly Participant[] _seats = new Participant[MaxSeats];
        readonly int[] _byName = new int[TelemetryCatalog.Count];
        readonly TelemetryStats _stats = new TelemetryStats();
        readonly List<TelemetryEvent> _batch = new List<TelemetryEvent>();
        readonly List<TelemetryEvent> _sinkBatch = new List<TelemetryEvent>();
        volatile bool _enabled;
        int _localConsent;
        long _autoKey;
        int _epoch;
        readonly int[] _consentVer = new int[MaxSeats];
        long _dropOverflowReport, _dropWithdrawReport;
        bool _disposed;

        public GameTelemetryService(TelemetryConfiguration config, TelemetrySessionContext context = null,
            Func<double> monotonicSeconds = null, Func<DateTime> utcNow = null, Func<ulong> random64 = null, params ITelemetrySink[] sinks)
        {
            _cfg = (config ?? new TelemetryConfiguration()).Clone().Normalize();
            Context = context ?? new TelemetrySessionContext();
            var sw = Stopwatch.StartNew();
            _clock = monotonicSeconds ?? (() => sw.Elapsed.TotalSeconds);
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _random64 = random64 ?? DefaultRandom;
            for (int i = 0; i < MaxSeats; i++) _seats[i] = new Participant();
            _enabled = _cfg.Enabled;
            if (sinks != null) foreach (var s in sinks) if (s != null) _sinks.Add(s);
            if (Context.Initializations == 0)
                Context.InitializeOnce(_cfg.Environment, TelemetryNames.RandomId("s-", _random64), _cfg.GroupId, _cfg.Cohort, _cfg.Experiments);
        }

        static readonly object RngLock = new object();
        static System.Security.Cryptography.RandomNumberGenerator _rng;
        static ulong DefaultRandom()
        {
            var b = new byte[8];
            lock (RngLock)
            {
                if (_rng == null) _rng = System.Security.Cryptography.RandomNumberGenerator.Create();
                _rng.GetBytes(b);
            }
            return BitConverter.ToUInt64(b, 0);
        }

        public TelemetryConfiguration Configuration { get { return _cfg; } }
        public TelemetrySessionContext Context { get; private set; }
        public bool IsEnabled { get { return _enabled; } }
        public bool HostMode { get; set; }
        public int LocalConsent { get { return _localConsent; } }
        public double Now { get { return _clock(); } }
        public IReadOnlyList<ITelemetrySink> Sinks { get { lock (_gate) return _sinks.ToArray(); } }
        public Func<ulong> Random64 { get { return _random64; } }

        /// <summary>Runtime on/off (config reload, tests). Disabling clears everything pending.</summary>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled && _cfg.Enabled;
            if (!enabled) lock (_gate) { _queue.Clear(); _private.Clear(); }
        }

        public void AddSink(ITelemetrySink sink)
        {
            if (sink == null) return;
            lock (_flushGate) lock (_gate) { if (!_sinks.Contains(sink)) _sinks.Add(sink); }
        }

        // ---- building ----------------------------------------------------------------------------

        public TelemetryEventBuilder Begin(string eventName)
        {
            if (!_enabled) return TelemetryEventBuilder.Null;
            var def = TelemetryCatalog.Find(eventName);
            if (def == null)
            {
                lock (_gate) _stats.Rejected++;
                return TelemetryEventBuilder.Null;
            }
            if (def.Availability == TelemetryAvailability.NotApplicable || def.Availability == TelemetryAvailability.Unsupported)
            {
                lock (_gate) _stats.Rejected++;
                return TelemetryEventBuilder.Null;
            }
            return new TelemetryEventBuilder(this, def);
        }

        /// <summary>Convenience for parameterless events.</summary>
        public bool EmitSimple(string eventName) { return Begin(eventName).Emit(); }

        internal bool Submit(EventDef def, TelemetryValue[] vals, bool invalid, string error, string logicalKey, double at)
        {
            if (!_enabled) return false;
            if (invalid)
            {
                lock (_gate) { _stats.Rejected++; _stats.Emitted++; }
                return false;
            }
            for (int i = 0; i < def.Params.Length; i++)
                if (!def.Params[i].Optional && !vals[i].HasValue)
                {
                    lock (_gate) { _stats.Rejected++; _stats.Emitted++; }
                    return false;
                }
            var e = new TelemetryEvent { Def = def, Values = vals };
            e.Source = def.Authority == TelemetryAuthority.Both ? (HostMode ? TelemetryAuthority.Host : TelemetryAuthority.Client) : def.Authority;
            e.TimestampUtc = _utcNow();
            e.Monotonic = double.IsNaN(at) ? _clock() : at;
            StampContext(e);
            for (int i = 0; i < def.Params.Length; i++)
                if (def.Params[i].Type == ParamType.Slot && vals[i].Kind == ValueKind.Int) { e.Subject = (int)vals[i].L; break; }
            bool keyed = e.Source == TelemetryAuthority.Host && logicalKey != null;
            if (e.Source == TelemetryAuthority.Host)
            {
                e.EventKey = TelemetryNames.HostEventKey(string.IsNullOrEmpty(e.MatchId) ? "s:" + e.SessionId : e.MatchId, def.Name, logicalKey ?? ("auto" + System.Threading.Interlocked.Increment(ref _autoKey)));
                e.EventId = TelemetryNames.HostEventId(e.EventKey);
            }
            else e.EventId = TelemetryNames.RandomId("c-", _random64);
            return Accept(e, keyed, false);
        }

        public bool Emit(TelemetryEvent e)
        {
            if (!_enabled || e == null) return false;
            return Accept(e, e.Source == TelemetryAuthority.Host, false);
        }

        void StampContext(TelemetryEvent e)
        {
            var c = Context;
            e.Environment = c.Environment;
            e.SessionId = c.SessionId;
            e.MatchId = c.MatchId;
            e.WeekId = c.WeekId;
            e.GroupId = c.GroupId;
            e.PlayerCount = c.PlayerCount;
            e.HasBots = c.HasBots;
            e.DayIndex = c.DayIndex;
            e.RoundIndex = c.RoundIndex;
            e.TaskNumber = c.TaskNumber;
            e.Phase = c.Phase;
            e.TaskId = c.TaskId;
            e.TaskVersion = c.TaskVersion;
            e.GameVersion = c.GameVersion;
            e.BuildVersion = c.BuildVersion;
            e.RulesetVersion = c.RulesetVersion;
            e.Cohort = c.Cohort;
            e.Experiments = c.Experiments;
        }

        // ---- consent -----------------------------------------------------------------------------

        public void SetLocalConsent(int level)
        {
            level = Math.Max(0, Math.Min(3, level));
            bool withdrawn, lowered;
            lock (_gate)
            {
                withdrawn = _localConsent >= 2 && level < 2;
                lowered = level < _localConsent;
                if (lowered) _epoch++;
                _localConsent = level;
                if (withdrawn)
                {
                    int local = Context.LocalSlot;
                    PurgeWhere(e => e.Source == TelemetryAuthority.Client || e.Def.Consent == ConsentRule.HostAggregate || (local >= 0 && e.References(local)), true);
                }
            }
            if (lowered) NotifySinksWithdrawn();
        }

        /// <summary>Cloud sinks drop their own pending data when consent was lowered (their queues are outside this service).</summary>
        void NotifySinksWithdrawn()
        {
            ITelemetrySink[] sinks;
            lock (_gate) sinks = _sinks.ToArray();
            foreach (var s in sinks) { try { s.OnConsentWithdrawn(); } catch (Exception) { } }
        }

        /// <summary>Changes whenever the slot's observed state (consent >= 2) flips; observers use it to discard partially observed windows.</summary>
        public int ConsentVersion(int slot)
        {
            if (slot < 0 || slot >= MaxSeats) return 0;
            lock (_gate) return _consentVer[slot];
        }

        public void SetParticipant(int slot, int consent, string anonymousId, bool isBot)
        {
            if (slot < 0 || slot >= MaxSeats) return;
            lock (_gate)
            {
                var p = _seats[slot];
                bool observedBefore = p.Present && p.Consent >= 2;
                bool withdrawn = p.Present && !p.IsBot && p.Consent >= 2 && consent < 2;
                p.Present = true;
                p.IsBot = isBot;
                p.Consent = isBot ? 2 : Math.Max(0, Math.Min(3, consent));
                p.AnonId = !isBot && p.Consent >= 2 && TelemetryNames.IsValidId(IdKind.PlayerId, anonymousId) ? anonymousId : "";
                if (observedBefore != (p.Present && p.Consent >= 2)) _consentVer[slot]++;
                if (withdrawn) { _epoch++; PurgeWhere(e => e.References(slot), true); }
            }
        }

        public void RemoveParticipant(int slot)
        {
            if (slot < 0 || slot >= MaxSeats) return;
            lock (_gate)
            {
                var p = _seats[slot];
                bool had = p.Present && p.Consent >= 2;
                if (had) { _consentVer[slot]++; _epoch++; }
                p.Present = false;
                p.Consent = 0;
                p.AnonId = "";
                p.IsBot = false;
                if (had) PurgeWhere(e => e.References(slot), true);
            }
        }

        public int ConsentOfSlot(int slot)
        {
            if (slot < 0 || slot >= MaxSeats) return 0;
            lock (_gate) return _seats[slot].Present ? _seats[slot].Consent : 0;
        }

        public string AnonymousIdOfSlot(int slot)
        {
            if (slot < 0 || slot >= MaxSeats) return "";
            lock (_gate) return _seats[slot].Present ? _seats[slot].AnonId : "";
        }

        /// <summary>Consenting non-bot humans / human players among the seated participants; -1 when none are seated.</summary>
        public double Coverage()
        {
            lock (_gate) return CoverageLocked();
        }

        double CoverageLocked()
        {
            int humans = 0, ok = 0;
            for (int i = 0; i < MaxSeats; i++)
            {
                var p = _seats[i];
                if (!p.Present || p.IsBot) continue;
                humans++;
                if (p.Consent >= 2) ok++;
            }
            return humans == 0 ? -1 : (double)ok / humans;
        }

        int LevelOfLocked(int slot)
        {
            if (slot < 0 || slot >= MaxSeats) return 0;
            var p = _seats[slot];
            return p.Present ? p.Consent : 0;
        }

        /// <summary>Applies the event's consent rule. Returns false = suppress. May rewrite a vote target to "unconsented".</summary>
        bool Evaluate(TelemetryEvent e, out bool local, out bool cloud)
        {
            var def = e.Def;
            int level;
            cloud = false;
            local = false;
            switch (def.Consent)
            {
                case ConsentRule.HostAggregate:
                    level = _localConsent;
                    break;
                case ConsentRule.AllParticipants:
                    level = 3;
                    for (int i = 0; i < def.Params.Length; i++)
                    {
                        if (def.Params[i].Type == ParamType.Slot && e.Values[i].Kind == ValueKind.Int)
                            level = Math.Min(level, LevelOfLocked((int)e.Values[i].L));
                    }
                    if (e.Subject < 0) level = Math.Min(level, _localConsent);
                    break;
                case ConsentRule.Voter:
                    level = e.Subject >= 0 ? LevelOfLocked(e.Subject) : 0;
                    if (level >= 2)
                    {
                        for (int i = 0; i < def.Params.Length; i++)
                        {
                            if (def.Params[i].Type == ParamType.SlotOrEnum && e.Values[i].Kind == ValueKind.Int && LevelOfLocked((int)e.Values[i].L) < 2)
                                e.Values[i] = TelemetryValue.OfStr("unconsented");
                        }
                    }
                    break;
                default: // Self
                    level = e.Source == TelemetryAuthority.Host && e.Subject >= 0 ? LevelOfLocked(e.Subject) : _localConsent;
                    break;
            }
            if (level < 2) return false;
            bool localOk = _cfg.Local || _cfg.Cloud;
            if (!localOk) return false;
            local = _cfg.Local;
            // cloud copy needs every referenced participant AND the local machine at level 3
            cloud = _cfg.Cloud && (def.Routing & TelemetryRouting.Cloud) != 0 && level >= 3 && _localConsent >= 3;
            return true;
        }

        // ---- pipeline ----------------------------------------------------------------------------

        bool Accept(TelemetryEvent e, bool dedupe, bool force)
        {
            lock (_gate)
            {
                _stats.Emitted++;
                _byName[e.Def.Index]++;
                bool cloud, local;
                if (!Evaluate(e, out local, out cloud)) { _stats.Suppressed++; return false; }
                e.RouteLocal = local; e.RouteCloud = cloud;
                // identity of the subject
                if (e.Source == TelemetryAuthority.Host)
                {
                    if (e.Subject >= 0 && e.Subject < MaxSeats && _seats[e.Subject].Present)
                    {
                        var p = _seats[e.Subject];
                        e.PlayerSlot = e.Subject;
                        e.HasIsBot = true;
                        e.IsBot = p.IsBot;
                        e.PlayerId = p.AnonId;
                    }
                    if (e.Def.Consent == ConsentRule.HostAggregate) e.Coverage = CoverageLocked();
                }
                else
                {
                    e.PlayerSlot = Context.LocalSlot;
                    e.PlayerId = Context.LocalPlayerId;
                    e.HasIsBot = Context.LocalSlot >= 0;
                    e.IsBot = Context.LocalIsBot;
                }
                if (dedupe)
                {
                    if (_seen.Contains(e.EventKey)) { _stats.Duplicates++; return false; }
                    _seen.Add(e.EventKey);
                    _seenOrder.Enqueue(e.EventKey);
                    while (_seenOrder.Count > SeenCapacity) _seen.Remove(_seenOrder.Dequeue());
                }
                _stats.Accepted++;
                if (e.IsPrivate) { BufferPrivate(e); return true; }
                Enqueue(e, force);
                return true;
            }
        }

        void BufferPrivate(TelemetryEvent e)
        {
            if (_private.Count >= _cfg.PrivateBufferBound)
            {
                _private.RemoveAt(0);
                _stats.DroppedPrivateOverflow++;
                _dropOverflowReport++;
            }
            _private.Add(e);
        }

        void Enqueue(TelemetryEvent e, bool force)
        {
            if (!force && _queue.Count >= _cfg.QueueBound)
            {
                _stats.DroppedOverflow++;
                _dropOverflowReport++;
                if (_cfg.Overflow == OverflowPolicy.DropNewest) return;
                _queue.RemoveFirst();
            }
            _queue.AddLast(e);
        }

        /// <summary>Must hold _gate. Removes matching events from queue and private buffer; counts them.</summary>
        void PurgeWhere(Func<TelemetryEvent, bool> match, bool withdrawal)
        {
            int removed = 0;
            for (var n = _queue.First; n != null;)
            {
                var next = n.Next;
                if (match(n.Value)) { _queue.Remove(n); removed++; }
                n = next;
            }
            for (int i = _private.Count - 1; i >= 0; i--)
                if (match(_private[i])) { _private.RemoveAt(i); removed++; }
            if (removed > 0)
            {
                _stats.DroppedWithdrawal += removed;
                _dropWithdrawReport += removed;
            }
        }

        public void ReleasePrivate()
        {
            lock (_gate)
            {
                foreach (var e in _private) Enqueue(e, false); // bounded: overflow is counted and reported
                _private.Clear();
            }
        }

        public void DiscardPrivate()
        {
            lock (_gate) _private.Clear();
        }

        // ---- flushing ----------------------------------------------------------------------------

        /// <summary>Writes everything queued to the sinks. Safe from any thread; a throwing sink never propagates.</summary>
        public void Flush()
        {
            if (_disposed) return;
            lock (_flushGate)
            {
                if (!_enabled) return;
                ReportDrops();
                ITelemetrySink[] sinks;
                int batchEpoch;
                lock (_gate)
                {
                    _batch.Clear();
                    foreach (var e in _queue) _batch.Add(e);
                    _queue.Clear();
                    sinks = _sinks.ToArray();
                    Revalidate(_batch);
                    batchEpoch = _epoch;
                }
                if (_batch.Count == 0) { FlushSinks(sinks); return; }
                foreach (var sink in sinks)
                {
                    // consent may have been lowered since the batch was detached (or since the previous sink): check again
                    lock (_gate) { if (_epoch != batchEpoch) { Revalidate(_batch); batchEpoch = _epoch; } }
                    _sinkBatch.Clear();
                    foreach (var e in _batch)
                    {
                        if (e.Revoked) continue;
                        if (sink.Kind == TelemetrySinkKind.Cloud ? e.RouteCloud : (sink.Kind == TelemetrySinkKind.Debug || e.RouteLocal))
                            _sinkBatch.Add(e);
                    }
                    if (_sinkBatch.Count == 0) continue;
                    try
                    {
                        sink.Write(_sinkBatch);
                        if (sink.Kind == TelemetrySinkKind.Local) lock (_gate) _stats.Written += _sinkBatch.Count;
                        _faulted[sink] = false;
                    }
                    catch (Exception ex)
                    {
                        OnSinkFault(sink, ex);
                    }
                }
                FlushSinks(sinks);
                _batch.Clear();
                _sinkBatch.Clear();
            }
        }

        /// <summary>Must hold _gate. Re-applies the consent rules to events that are already accepted (delivery-time check).</summary>
        void Revalidate(List<TelemetryEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e.Revoked) continue;
                bool local, cloud;
                if (!Evaluate(e, out local, out cloud))
                {
                    e.Revoked = true;
                    _stats.DroppedWithdrawal++;
                    _dropWithdrawReport++;
                    continue;
                }
                e.RouteLocal = local;
                e.RouteCloud = cloud;
            }
        }

        void FlushSinks(ITelemetrySink[] sinks)
        {
            foreach (var s in sinks)
            {
                try { s.Flush(); }
                catch (Exception ex) { OnSinkFault(s, ex); }
            }
        }

        void OnSinkFault(ITelemetrySink sink, Exception ex)
        {
            bool wasFaulted;
            lock (_gate)
            {
                _stats.SinkErrors++;
                _faulted.TryGetValue(sink, out wasFaulted);
                _faulted[sink] = true;
            }
            if (wasFaulted) return; // report each fault once, not every retry
            string name = sink.Name == "local_jsonl" || sink.Name == "debug" || sink.Name == "unity_analytics" ? sink.Name : "other";
            string cat = ex is IOException || ex is UnauthorizedAccessException ? "io" : "exception";
            var b = Begin("telemetry_sink_error");
            b.Enum("sink", name).Enum("category", cat);
            b.EmitForced();
        }

        void ReportDrops()
        {
            long over, withdrawn;
            lock (_gate)
            {
                over = _dropOverflowReport; _dropOverflowReport = 0;
                withdrawn = _dropWithdrawReport; _dropWithdrawReport = 0;
            }
            if (over > 0) Begin("telemetry_queue_dropped").Int("dropped", over).Enum("reason", "overflow").EmitForced();
            if (withdrawn > 0) Begin("telemetry_queue_dropped").Int("dropped", withdrawn).Enum("reason", "consent_withdrawn").EmitForced();
        }

        internal bool SubmitForced(EventDef def, TelemetryValue[] vals, bool invalid)
        {
            if (!_enabled || invalid) return false;
            var e = new TelemetryEvent { Def = def, Values = vals };
            e.Source = HostMode ? TelemetryAuthority.Host : TelemetryAuthority.Client;
            e.TimestampUtc = _utcNow();
            e.Monotonic = _clock();
            StampContext(e);
            if (e.Source == TelemetryAuthority.Host)
            {
                e.EventKey = TelemetryNames.HostEventKey(string.IsNullOrEmpty(e.MatchId) ? "s:" + e.SessionId : e.MatchId, def.Name, "auto" + System.Threading.Interlocked.Increment(ref _autoKey));
                e.EventId = TelemetryNames.HostEventId(e.EventKey);
            }
            else e.EventId = TelemetryNames.RandomId("c-", _random64);
            return Accept(e, false, true);
        }

        // ---- stats / lifetime --------------------------------------------------------------------

        public TelemetryStats GetStats()
        {
            lock (_gate)
            {
                var s = new TelemetryStats
                {
                    Emitted = _stats.Emitted, Accepted = _stats.Accepted, Written = _stats.Written, Suppressed = _stats.Suppressed,
                    Rejected = _stats.Rejected, Duplicates = _stats.Duplicates, DroppedOverflow = _stats.DroppedOverflow,
                    DroppedPrivateOverflow = _stats.DroppedPrivateOverflow, DroppedWithdrawal = _stats.DroppedWithdrawal,
                    SinkErrors = _stats.SinkErrors, QueueSize = _queue.Count, PrivateBuffered = _private.Count,
                };
                for (int i = 0; i < _byName.Length; i++) if (_byName[i] > 0) s.PerName[TelemetryCatalog.All[i].Name] = _byName[i];
                return s;
            }
        }

        /// <summary>Final flush; pending private events are released only if <paramref name="releasePrivate"/> (never on a crash path).</summary>
        public void Shutdown(bool releasePrivate = false)
        {
            if (_disposed) return;
            if (releasePrivate) ReleasePrivate();
            try { Flush(); } catch (Exception) { }
            _disposed = true;
            ITelemetrySink[] sinks;
            lock (_gate) sinks = _sinks.ToArray();
            foreach (var s in sinks) { try { s.Dispose(); } catch (Exception) { } }
        }

        public void Dispose() { Shutdown(false); }
    }
}
