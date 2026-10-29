using System;
using System.Globalization;
using System.Text;

namespace BirimizKovulacak.Voice
{
    /// <summary>
    /// Periodic voice statistics for the match log (automated runs) and bug reports. Reads the cumulative
    /// counters of the pipeline objects and reports deltas per window. Formatting allocates a string, but only once
    /// per window (default 10 s), never per frame.
    /// </summary>
    public sealed class VoiceStatsReporter
    {
        struct RxSnap { public long Received, Bytes, Played, Concealed, Fec, Late, Dup, Overflow, Skipped, DecodeErrors; public int Underruns; }

        readonly StringBuilder _sb = new StringBuilder(512);
        readonly RxSnap[] _rx = new RxSnap[VoiceFormat.MaxPlayers];
        long _txFrames, _txBytes, _txErrors, _txSpurts;
        long _upMsgs, _upBytes, _downMsgs, _downBytes;
        long _rtDecisions, _rtDelivered, _rtCall, _rtTap, _rtMuffled, _rtMuted, _rtRange, _rtRateLimited, _rtUnknown;
        long _recFrames, _recDropped;
        double _lastMs = -1;

        public static string Kbps(long bytes, double seconds)
        {
            return seconds <= 0 ? "0" : (bytes * 8.0 / seconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Builds one log line with the deltas since the previous call, or null when nothing happened.
        /// Any argument may be null.
        /// </summary>
        public string Build(double nowMs, string codec, VoiceCapturePipeline tx, VoicePlaybackChannel[] channels,
            PcmPlayoutReader[] readers, ProximityVoiceRouter router, VoiceTransportCounters net, VoiceCallRecorder recorder = null)
        {
            double seconds = _lastMs < 0 ? 0 : (nowMs - _lastMs) / 1000.0;
            _lastMs = nowMs;
            _sb.Length = 0;
            bool any = false;
            _sb.Append("voice stats window=").Append(seconds.ToString("0.0", CultureInfo.InvariantCulture)).Append("s codec=").Append(codec);

            if (tx != null)
            {
                long frames = tx.FramesEncoded - _txFrames, bytes = tx.BytesEncoded - _txBytes;
                long errors = tx.EncodeErrors - _txErrors, spurts = tx.Spurts - _txSpurts;
                _txFrames = tx.FramesEncoded; _txBytes = tx.BytesEncoded; _txErrors = tx.EncodeErrors; _txSpurts = tx.Spurts;
                if (frames > 0 || errors > 0) any = true;
                double talkSec = frames * VoiceFormat.FrameMs / 1000.0;
                _sb.Append(" | tx frames=").Append(frames).Append(" spurts=").Append(spurts).Append(" errors=").Append(errors)
                   .Append(" opus_kbps_while_talking=").Append(Kbps(bytes, talkSec))
                   .Append(" stream_kbps_while_talking=").Append(Kbps(bytes + frames * VoiceWire.FrameHeaderBytes, talkSec));
            }

            if (net != null)
            {
                long um = net.UpMessages - _upMsgs, ub = net.UpBytes - _upBytes, dm = net.DownMessages - _downMsgs, db = net.DownBytes - _downBytes;
                _upMsgs = net.UpMessages; _upBytes = net.UpBytes; _downMsgs = net.DownMessages; _downBytes = net.DownBytes;
                if (um + dm > 0) any = true;
                _sb.Append(" | net up_msgs=").Append(um).Append(" up_kbps=").Append(Kbps(ub, seconds))
                   .Append(" down_msgs=").Append(dm).Append(" down_kbps=").Append(Kbps(db, seconds));
            }

            if (router != null)
            {
                long d = router.Decisions - _rtDecisions, del = router.Delivered - _rtDelivered, c = router.CallRoutes - _rtCall, tp = router.TapRoutes - _rtTap;
                long m = router.MuffledRoutes - _rtMuffled, mu = router.MutedDrops - _rtMuted, rg = router.OutOfRange - _rtRange;
                _rtDecisions = router.Decisions; _rtDelivered = router.Delivered; _rtCall = router.CallRoutes; _rtTap = router.TapRoutes;
                _rtMuffled = router.MuffledRoutes; _rtMuted = router.MutedDrops; _rtRange = router.OutOfRange;
                long rl = 0, unk = 0;
                if (net != null)
                {
                    rl = net.RateLimited - _rtRateLimited; unk = net.UnknownSender - _rtUnknown;
                    _rtRateLimited = net.RateLimited; _rtUnknown = net.UnknownSender;
                }
                if (d > 0) any = true;
                _sb.Append(" | route decisions=").Append(d).Append(" delivered=").Append(del).Append(" call=").Append(c).Append(" tap=").Append(tp)
                   .Append(" muffled=").Append(m).Append(" muted=").Append(mu).Append(" out_of_range=").Append(rg)
                   .Append(" rate_limited=").Append(rl).Append(" unknown_sender=").Append(unk);
            }

            if (recorder != null && recorder.Enabled)
            {
                long rf = recorder.RecordedFrames - _recFrames, rd = recorder.DroppedFrames - _recDropped;
                _recFrames = recorder.RecordedFrames; _recDropped = recorder.DroppedFrames;
                if (rf > 0 || rd > 0) any = true;
                _sb.Append(" | rec frames=").Append(rf).Append(" dropped=").Append(rd)
                   .Append(" calls=").Append(recorder.CallCount)
                   .Append(" used_kb=").Append(recorder.UsedBytes / 1024)
                   .Append(" reserved_kb=").Append(recorder.ReservedBytes / 1024);
            }

            if (channels != null)
            {
                for (int i = 0; i < channels.Length && i < _rx.Length; i++)
                {
                    var ch = channels[i];
                    if (ch == null) continue;
                    var j = ch.Jitter;
                    var s = _rx[i];
                    int underruns = readers != null && i < readers.Length && readers[i] != null ? readers[i].Underruns : 0;
                    long rec = j.Received - s.Received;
                    if (rec == 0 && j.Played == s.Played) { continue; }
                    any = true;
                    _sb.Append(" | rx s").Append(i)
                       .Append(" pkts=").Append(rec)
                       .Append(" kbps=").Append(Kbps(ch.BytesReceived - s.Bytes, seconds))
                       .Append(" played=").Append(j.Played - s.Played)
                       .Append(" lost=").Append(j.Concealed - s.Concealed)
                       .Append(" fec=").Append(j.FecRecovered - s.Fec)
                       .Append(" late=").Append(j.Late - s.Late)
                       .Append(" dup=").Append(j.Duplicates - s.Dup)
                       .Append(" dropped=").Append((j.Overflow - s.Overflow) + (j.Skipped - s.Skipped))
                       .Append(" decode_err=").Append(ch.DecodeErrors - s.DecodeErrors)
                       .Append(" underruns=").Append(underruns - s.Underruns)
                       .Append(" jitter_ms=").Append(j.JitterMs.ToString("0.0", CultureInfo.InvariantCulture))
                       .Append(" target_ms=").Append(j.TargetFrames * VoiceFormat.FrameMs);
                    s.Received = j.Received; s.Bytes = ch.BytesReceived; s.Played = j.Played; s.Concealed = j.Concealed;
                    s.Fec = j.FecRecovered; s.Late = j.Late; s.Dup = j.Duplicates; s.Overflow = j.Overflow; s.Skipped = j.Skipped;
                    s.DecodeErrors = ch.DecodeErrors; s.Underruns = underruns;
                    _rx[i] = s;
                }
            }
            return any ? _sb.ToString() : null;
        }
    }

    /// <summary>Transport counters filled by the network glue (cumulative).</summary>
    public sealed class VoiceTransportCounters
    {
        public long UpMessages, UpBytes, DownMessages, DownBytes, RateLimited, UnknownSender, Malformed;
    }

    /// <summary>Per-sender token bucket on the host (protects routing from a flooding client).</summary>
    public sealed class VoiceRateLimiter
    {
        readonly double[] _tokens = new double[VoiceFormat.MaxPlayers];
        readonly double[] _lastMs = new double[VoiceFormat.MaxPlayers];
        public double FramesPerSecond = 60;
        public double Burst = 25;

        public bool Allow(int slot, double nowMs)
        {
            if (slot < 0 || slot >= _tokens.Length) return false;
            double t = _tokens[slot] + (nowMs - _lastMs[slot]) * FramesPerSecond / 1000.0;
            if (t > Burst) t = Burst;
            _lastMs[slot] = nowMs;
            if (t < 1.0) { _tokens[slot] = t; return false; }
            _tokens[slot] = t - 1.0;
            return true;
        }
    }

    /// <summary>Voice launch options (parsed by VoiceSystem itself, so LaunchArgs needs no change).</summary>
    public sealed class VoiceLaunchOptions
    {
        /// <summary>-voicetest sine|speech|path.wav : synthetic source instead of the microphone.</summary>
        public string TestSource;
        /// <summary>-voicecodec auto|native|managed</summary>
        public VoiceCodecBackend Codec = VoiceCodecBackend.Auto;
        /// <summary>-voicestats seconds (0 = off). Default 30 s, or 10 s with -voicetest.</summary>
        public float StatsSeconds = -1;
        /// <summary>-novoice : disable capture and playback entirely.</summary>
        public bool Disabled;
        /// <summary>-voiceloopback : hear your own encoded voice (codec/device check without a second client).</summary>
        public bool Loopback;

        public float EffectiveStatsSeconds => StatsSeconds >= 0 ? StatsSeconds : (string.IsNullOrEmpty(TestSource) ? 30f : 10f);

        public static VoiceLaunchOptions Parse(string[] args)
        {
            var o = new VoiceLaunchOptions();
            if (args == null) return o;
            for (int i = 0; i < args.Length; i++)
            {
                string k = (args[i] ?? "").ToLowerInvariant();
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (k)
                {
                    case "-voicetest":
                        o.TestSource = string.IsNullOrEmpty(next) || next.StartsWith("-") ? "sine" : next;
                        if (o.TestSource == next) i++;
                        break;
                    case "-voicecodec":
                        if (next != null)
                        {
                            string v = next.ToLowerInvariant();
                            o.Codec = v == "native" ? VoiceCodecBackend.Native : v == "managed" ? VoiceCodecBackend.Managed : VoiceCodecBackend.Auto;
                            i++;
                        }
                        break;
                    case "-voicestats":
                        float s;
                        if (next != null && float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out s)) { o.StatsSeconds = s; i++; }
                        break;
                    case "-novoice": o.Disabled = true; break;
                    case "-voiceloopback": o.Loopback = true; break;
                }
            }
            return o;
        }
    }
}
