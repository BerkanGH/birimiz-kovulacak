using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// Explicit v5 little-endian wire schema. The header is magic, UInt16 protocol, message kind.
    /// Enums and booleans occupy one byte. Strings/lists use unsigned LEB128 count + 1 (zero = null).
    /// Reference objects use a strict presence byte. No runtime reflection or v2 dependencies.
    /// </summary>
    public static class LineCodec
    {
        private const byte Magic = 0xB3;
        private const int MaxPayload = 1024 * 1024;
        private const int MaxItems = 4096;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] EncodeView(LineView v) { return Encode(1, w => WriteLineView(w, v)); }
        public static LineView DecodeView(byte[] payload) { return Decode(payload, 1, ReadLineView); }
        public static byte[] EncodeAction(LineAction a) { return Encode(2, w => WriteLineAction(w, a)); }
        public static LineAction DecodeAction(byte[] payload) { return Decode(payload, 2, ReadLineAction); }
        public static byte[] EncodeResult(LineActionResult r) { return Encode(3, w => WriteLineActionResult(w, r)); }
        public static LineActionResult DecodeResult(byte[] payload) { return Decode(payload, 3, ReadLineActionResult); }
        public static byte[] EncodeEvents(List<LineEvent> events) { return Encode(4, w => WriteList(w, events, WriteLineEvent)); }
        public static List<LineEvent> DecodeEvents(byte[] payload) { return Decode(payload, 4, r => ReadList(r, ReadLineEvent)); }
        public static byte[] EncodeLobby(LineLobbyView lobby) { return Encode(5, w => WriteLineLobbyView(w, lobby)); }
        public static LineLobbyView DecodeLobby(byte[] payload) { return Decode(payload, 5, ReadLineLobbyView); }
        public static byte[] EncodeWelcome(LineWelcome w) { return Encode(6, b => WriteLineWelcome(b, w)); }
        public static LineWelcome DecodeWelcome(byte[] payload) { return Decode(payload, 6, ReadLineWelcome); }
        public static byte[] EncodeConnection(LineConnectionPayload p) { return Encode(7, w => WriteLineConnectionPayload(w, p)); }
        public static LineConnectionPayload DecodeConnection(byte[] payload) { return Decode(payload, 7, ReadLineConnectionPayload); }

        public static byte[] EncodeMessages(List<LineMessage> m) { return Encode(12, w => WriteList(w, m, WriteLineMessage)); }
        public static List<LineMessage> DecodeMessages(byte[] b) { return Decode(b, 12, r => ReadList(r, ReadLineMessage)); }

        internal static LineView Clone(LineView value) { return DecodeView(EncodeView(value)); }
        internal static LineSetup Clone(LineSetup value) { return Decode(8, value, WriteLineSetup, ReadLineSetup); }
        internal static LineConfig Clone(LineConfig value) { return Decode(9, value, WriteLineConfig, ReadLineConfig); }
        internal static LineSessionHistory Clone(LineSessionHistory value) { return Decode(10, value, WriteLineSessionHistory, ReadLineSessionHistory); }
        internal static LineWeekResult Clone(LineWeekResult value) { return Decode(11, value, WriteLineWeekResult, ReadLineWeekResult); }

        internal static ulong HashResult(LineWeekResult value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var copy = Clone(value);
            copy.ResultHash = 0; copy.MatchId = "";
            byte[] bytes = Encode(11, w => WriteLineWeekResult(w, copy));
            ulong hash = 14695981039346656037UL;
            unchecked { for (int i = 0; i < bytes.Length; i++) hash = (hash ^ bytes[i]) * 1099511628211UL; }
            return hash;
        }

        private static T Decode<T>(byte kind, T value, Action<BinaryWriter, T> write, Func<BinaryReader, T> read)
        {
            return Decode(Encode(kind, w => write(w, value)), kind, read);
        }

        private static byte[] Encode(byte kind, Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(Magic); writer.Write((ushort)LineProtocol.Version); writer.Write(kind);
                write(writer); writer.Flush();
                if (stream.Length > MaxPayload) throw new FormatException("Payload too large.");
                return stream.ToArray();
            }
        }

        private static T Decode<T>(byte[] payload, byte kind, Func<BinaryReader, T> read)
        {
            if (payload == null || payload.Length > MaxPayload) throw new FormatException("Invalid payload.");
            try
            {
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadByte() != Magic || reader.ReadUInt16() != LineProtocol.Version || reader.ReadByte() != kind)
                        throw new FormatException("Incompatible message (magic, protocol version or kind).");
                    T value = read(reader);
                    if (stream.Position != stream.Length) throw new FormatException("Trailing payload bytes.");
                    return value;
                }
            }
            catch (IOException ex) { throw new FormatException("Truncated payload.", ex); }
            catch (ArgumentException ex) { throw new FormatException("Malformed payload.", ex); }
            catch (OverflowException ex) { throw new FormatException("Invalid size.", ex); }
        }

        private static bool ReadBool(BinaryReader r)
        {
            byte b = r.ReadByte();
            if (b > 1) throw new FormatException("Invalid boolean.");
            return b == 1;
        }

        private static byte ReadEnum(BinaryReader r, byte max, string name)
        {
            byte b = r.ReadByte();
            if (b > max) throw new FormatException("Unknown " + name + ".");
            return b;
        }

        private static void WriteEnum(BinaryWriter w, byte value, byte max, string name)
        {
            if (value > max) throw new FormatException("Unknown " + name + ".");
            w.Write(value);
        }

        private static void WriteLength(BinaryWriter w, int count)
        {
            uint n = checked((uint)(count + 1));
            while (n >= 128) { w.Write((byte)(n | 128)); n >>= 7; }
            w.Write((byte)n);
        }

        private static int ReadLength(BinaryReader r, int limit)
        {
            uint value = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                byte b = r.ReadByte();
                if (shift == 28 && (b & 0xF0) != 0) throw new FormatException("Length overflow.");
                value |= (uint)(b & 127) << shift;
                if ((b & 128) == 0)
                {
                    if (shift > 0 && b == 0) throw new FormatException("Noncanonical length.");
                    if (value > (uint)limit + 1) throw new FormatException("Length exceeds limit.");
                    return (int)value - 1;
                }
            }
            throw new FormatException("Invalid length.");
        }

        private static void WriteString(BinaryWriter w, string value)
        {
            if (value == null) { WriteLength(w, -1); return; }
            byte[] bytes = Utf8.GetBytes(value);
            if (bytes.Length > 2048) throw new FormatException("String too long.");
            WriteLength(w, bytes.Length); w.Write(bytes);
        }

        private static string ReadString(BinaryReader r)
        {
            int length = ReadLength(r, 2048);
            if (length < 0) return null;
            if (length > r.BaseStream.Length - r.BaseStream.Position) throw new FormatException("Truncated string.");
            return Utf8.GetString(r.ReadBytes(length));
        }

        private static void WriteList<T>(BinaryWriter w, List<T> values, Action<BinaryWriter, T> write)
        {
            if (values == null) { WriteLength(w, -1); return; }
            if (values.Count > MaxItems) throw new FormatException("List too long.");
            WriteLength(w, values.Count);
            for (int i = 0; i < values.Count; i++) write(w, values[i]);
        }

        private static List<T> ReadList<T>(BinaryReader r, Func<BinaryReader, T> read)
        {
            int count = ReadLength(r, MaxItems);
            if (count < 0) return null;
            if (count > r.BaseStream.Length - r.BaseStream.Position) throw new FormatException("Truncated list.");
            var values = new List<T>(count);
            for (int i = 0; i < count; i++) values.Add(read(r));
            return values;
        }

        // ---- enums (contiguous values 0..max; anything else is rejected)
        private static LinePhase ReadLinePhase(BinaryReader r) { return (LinePhase)ReadEnum(r, 17, "LinePhase"); }
        private static void WriteLinePhase(BinaryWriter w, LinePhase v) { WriteEnum(w, (byte)v, 17, "LinePhase"); }
        private static TaskKind ReadTaskKind(BinaryReader r) { return (TaskKind)ReadEnum(r, 3, "TaskKind"); }
        private static void WriteTaskKind(BinaryWriter w, TaskKind v) { WriteEnum(w, (byte)v, 3, "TaskKind"); }
        private static TaskFamily ReadTaskFamily(BinaryReader r) { return (TaskFamily)ReadEnum(r, 2, "TaskFamily"); }
        private static void WriteTaskFamily(BinaryWriter w, TaskFamily v) { WriteEnum(w, (byte)v, 2, "TaskFamily"); }
        private static CallStatus ReadCallStatus(BinaryReader r) { return (CallStatus)ReadEnum(r, 3, "CallStatus"); }
        private static void WriteCallStatus(BinaryWriter w, CallStatus v) { WriteEnum(w, (byte)v, 3, "CallStatus"); }
        private static CallEndReason ReadCallEndReason(BinaryReader r) { return (CallEndReason)ReadEnum(r, 5, "CallEndReason"); }
        private static void WriteCallEndReason(BinaryWriter w, CallEndReason v) { WriteEnum(w, (byte)v, 5, "CallEndReason"); }
        private static LineVerb ReadLineVerb(BinaryReader r) { return (LineVerb)ReadEnum(r, 14, "LineVerb"); }
        private static void WriteLineVerb(BinaryWriter w, LineVerb v) { WriteEnum(w, (byte)v, 14, "LineVerb"); }
        private static LineFailure ReadLineFailure(BinaryReader r) { return (LineFailure)ReadEnum(r, 23, "LineFailure"); }
        private static void WriteLineFailure(BinaryWriter w, LineFailure v) { WriteEnum(w, (byte)v, 23, "LineFailure"); }
        private static LineAudience ReadLineAudience(BinaryReader r) { return (LineAudience)ReadEnum(r, 2, "LineAudience"); }
        private static void WriteLineAudience(BinaryWriter w, LineAudience v) { WriteEnum(w, (byte)v, 2, "LineAudience"); }
        private static LineEventType ReadLineEventType(BinaryReader r) { return (LineEventType)ReadEnum(r, 18, "LineEventType"); }
        private static void WriteLineEventType(BinaryWriter w, LineEventType v) { WriteEnum(w, (byte)v, 18, "LineEventType"); }
        private static BlameReason ReadBlameReason(BinaryReader r) { return (BlameReason)ReadEnum(r, 6, "BlameReason"); }
        private static void WriteBlameReason(BinaryWriter w, BlameReason v) { WriteEnum(w, (byte)v, 6, "BlameReason"); }
        private static PrimReason ReadPrimReason(BinaryReader r) { return (PrimReason)ReadEnum(r, 9, "PrimReason"); }
        private static void WritePrimReason(BinaryWriter w, PrimReason v) { WriteEnum(w, (byte)v, 9, "PrimReason"); }
        private static MessageKind ReadMessageKind(BinaryReader r) { return (MessageKind)ReadEnum(r, 3, "MessageKind"); }
        private static void WriteMessageKind(BinaryWriter w, MessageKind v) { WriteEnum(w, (byte)v, 3, "MessageKind"); }
        private static DossierKind ReadDossierKind(BinaryReader r) { return (DossierKind)ReadEnum(r, 8, "DossierKind"); }
        private static void WriteDossierKind(BinaryWriter w, DossierKind v) { WriteEnum(w, (byte)v, 8, "DossierKind"); }
        private static BallotKind ReadBallotKind(BinaryReader r) { return (BallotKind)ReadEnum(r, 3, "BallotKind"); }
        private static void WriteBallotKind(BinaryWriter w, BallotKind v) { WriteEnum(w, (byte)v, 3, "BallotKind"); }
        private static FiringDecision ReadFiringDecision(BinaryReader r) { return (FiringDecision)ReadEnum(r, 5, "FiringDecision"); }
        private static void WriteFiringDecision(BinaryWriter w, FiringDecision v) { WriteEnum(w, (byte)v, 5, "FiringDecision"); }

        // ---- contract types (declaration order)
        internal static void WriteLineAction(BinaryWriter w, LineAction v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.WeekId);
            w.Write(v.RequestId);
            WriteLineVerb(w, v.Verb);
            w.Write(v.Target);
            w.Write(v.A);
            w.Write(v.B);
            w.Write(v.C);
            WriteString(w, v.Text);
        }
        internal static LineAction ReadLineAction(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineAction();
            v.WeekId = r.ReadInt32();
            v.RequestId = r.ReadUInt32();
            v.Verb = ReadLineVerb(r);
            v.Target = r.ReadInt32();
            v.A = r.ReadInt32();
            v.B = r.ReadInt32();
            v.C = r.ReadInt32();
            v.Text = ReadString(r);
            return v;
        }

        internal static void WriteLineActionResult(BinaryWriter w, LineActionResult v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.RequestId);
            w.Write(v.Accepted);
            WriteLineFailure(w, v.Failure);
        }
        internal static LineActionResult ReadLineActionResult(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineActionResult();
            v.RequestId = r.ReadUInt32();
            v.Accepted = ReadBool(r);
            v.Failure = ReadLineFailure(r);
            return v;
        }

        internal static void WriteLineEvent(BinaryWriter w, LineEvent v)
        {
            w.Write(v != null); if (v == null) return;
            WriteLineEventType(w, v.Type);
            w.Write(v.Time);
            WriteLineAudience(w, v.Audience);
            w.Write(v.AudiencePlayer);
            w.Write(v.Actor);
            w.Write(v.Target);
            w.Write(v.A);
            w.Write(v.B);
            w.Write(v.C);
        }
        internal static LineEvent ReadLineEvent(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineEvent();
            v.Type = ReadLineEventType(r);
            v.Time = r.ReadDouble();
            v.Audience = ReadLineAudience(r);
            v.AudiencePlayer = r.ReadInt32();
            v.Actor = r.ReadInt32();
            v.Target = r.ReadInt32();
            v.A = r.ReadInt32();
            v.B = r.ReadInt32();
            v.C = r.ReadInt32();
            return v;
        }

        internal static void WriteLinePlayerInfo(BinaryWriter w, LinePlayerInfo v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Id);
            WriteString(w, v.Name);
            w.Write(v.ColorIndex);
            w.Write(v.IsBot);
        }
        internal static LinePlayerInfo ReadLinePlayerInfo(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LinePlayerInfo();
            v.Id = r.ReadInt32();
            v.Name = ReadString(r);
            v.ColorIndex = r.ReadInt32();
            v.IsBot = ReadBool(r);
            return v;
        }

        internal static void WriteLineSetup(BinaryWriter w, LineSetup v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.WeekId);
            WriteString(w, v.MatchId);
            w.Write(v.Seed);
            w.Write(v.StartTime);
            WriteList(w, v.Players, (b, x) => { WriteLinePlayerInfo(b, x); });
            w.Write(v.RecordingsEnabled);
            WriteLineSessionHistory(w, v.History);
            WriteList(w, v.ForceTasks, (b, x) => { WriteTaskKind(b, x); });
        }
        internal static LineSetup ReadLineSetup(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineSetup();
            v.WeekId = r.ReadInt32();
            v.MatchId = ReadString(r);
            v.Seed = r.ReadInt32();
            v.StartTime = r.ReadDouble();
            v.Players = ReadList(r, b => ReadLinePlayerInfo(b));
            v.RecordingsEnabled = ReadBool(r);
            v.History = ReadLineSessionHistory(r);
            v.ForceTasks = ReadList(r, b => ReadTaskKind(b));
            return v;
        }

        internal static void WriteLineSessionHistory(BinaryWriter w, LineSessionHistory v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.WeeksPlayed);
            WriteList(w, v.Wins, (b, x) => { b.Write(x); });
            WriteList(w, v.TimesFired, (b, x) => { b.Write(x); });
        }
        internal static LineSessionHistory ReadLineSessionHistory(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineSessionHistory();
            v.WeeksPlayed = r.ReadInt32();
            v.Wins = ReadList(r, b => b.ReadInt32());
            v.TimesFired = ReadList(r, b => b.ReadInt32());
            return v;
        }

        internal static void WriteLineLobbyView(BinaryWriter w, LineLobbyView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.ProtocolVersion);
            WriteString(w, v.BuildId);
            WriteString(w, v.RulesetVersion);
            WriteList(w, v.Slots, (b, x) => { WriteLineLobbySlot(b, x); });
            w.Write(v.WeekRunning);
            w.Write(v.WeekId);
            w.Write(v.RecordingsEnabled);
            w.Write(v.WeeksPlayed);
            WriteString(w, v.StatusKey);
            WriteString(w, v.SessionId);
            WriteString(w, v.GroupId);
            WriteList(w, v.Experiments, (b, x) => { WriteString(b, x); });
        }
        internal static LineLobbyView ReadLineLobbyView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineLobbyView();
            v.ProtocolVersion = r.ReadInt32();
            v.BuildId = ReadString(r);
            v.RulesetVersion = ReadString(r);
            v.Slots = ReadList(r, b => ReadLineLobbySlot(b));
            v.WeekRunning = ReadBool(r);
            v.WeekId = r.ReadInt32();
            v.RecordingsEnabled = ReadBool(r);
            v.WeeksPlayed = r.ReadInt32();
            v.StatusKey = ReadString(r);
            v.SessionId = ReadString(r);
            v.GroupId = ReadString(r);
            v.Experiments = ReadList(r, b => ReadString(b));
            return v;
        }

        internal static void WriteLineLobbySlot(BinaryWriter w, LineLobbySlot v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Slot);
            w.Write(v.Occupied);
            w.Write(v.Connected);
            w.Write(v.Ready);
            w.Write(v.IsHost);
            w.Write(v.IsBot);
            WriteString(w, v.Name);
            w.Write(v.Wins);
            w.Write(v.WantsRematch);
        }
        internal static LineLobbySlot ReadLineLobbySlot(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineLobbySlot();
            v.Slot = r.ReadInt32();
            v.Occupied = ReadBool(r);
            v.Connected = ReadBool(r);
            v.Ready = ReadBool(r);
            v.IsHost = ReadBool(r);
            v.IsBot = ReadBool(r);
            v.Name = ReadString(r);
            v.Wins = r.ReadInt32();
            v.WantsRematch = ReadBool(r);
            return v;
        }

        internal static void WriteLineConnectionPayload(BinaryWriter w, LineConnectionPayload v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.ProtocolVersion);
            WriteString(w, v.BuildId);
            WriteString(w, v.PlayerName);
            WriteString(w, v.ReconnectToken);
            WriteString(w, v.Language);
            w.Write(v.TelemetryConsent);
            WriteString(w, v.AnonymousPlayerId);
        }
        internal static LineConnectionPayload ReadLineConnectionPayload(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineConnectionPayload();
            v.ProtocolVersion = r.ReadInt32();
            v.BuildId = ReadString(r);
            v.PlayerName = ReadString(r);
            v.ReconnectToken = ReadString(r);
            v.Language = ReadString(r);
            v.TelemetryConsent = r.ReadInt32();
            v.AnonymousPlayerId = ReadString(r);
            return v;
        }

        internal static void WriteLineWelcome(BinaryWriter w, LineWelcome v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Slot);
            WriteString(w, v.ReconnectToken);
            w.Write(v.Reconnected);
            w.Write(v.WeekId);
        }
        internal static LineWelcome ReadLineWelcome(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineWelcome();
            v.Slot = r.ReadInt32();
            v.ReconnectToken = ReadString(r);
            v.Reconnected = ReadBool(r);
            v.WeekId = r.ReadInt32();
            return v;
        }

        internal static void WriteLineDurations(BinaryWriter w, LineDurations v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Intro);
            w.Write(v.TaskPlay);
            w.Write(v.TaskReveal);
            w.Write(v.Break);
            w.Write(v.MeetingDossier);
            w.Write(v.MeetingDiscussion);
            w.Write(v.MeetingVote);
            w.Write(v.MeetingVerdict);
            w.Write(v.CrisisCalls);
            w.Write(v.CrisisVote);
            w.Write(v.CrisisVerdict);
            w.Write(v.FinalDiscussion);
            w.Write(v.FinalDefense);
            w.Write(v.FinalVote);
            w.Write(v.FinalRevote);
            w.Write(v.FinalVerdict);
            w.Write(v.Results);
            w.Write(v.Ring);
        }
        internal static LineDurations ReadLineDurations(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineDurations();
            v.Intro = r.ReadSingle();
            v.TaskPlay = r.ReadSingle();
            v.TaskReveal = r.ReadSingle();
            v.Break = r.ReadSingle();
            v.MeetingDossier = r.ReadSingle();
            v.MeetingDiscussion = r.ReadSingle();
            v.MeetingVote = r.ReadSingle();
            v.MeetingVerdict = r.ReadSingle();
            v.CrisisCalls = r.ReadSingle();
            v.CrisisVote = r.ReadSingle();
            v.CrisisVerdict = r.ReadSingle();
            v.FinalDiscussion = r.ReadSingle();
            v.FinalDefense = r.ReadSingle();
            v.FinalVote = r.ReadSingle();
            v.FinalRevote = r.ReadSingle();
            v.FinalVerdict = r.ReadSingle();
            v.Results = r.ReadSingle();
            v.Ring = r.ReadSingle();
            return v;
        }

        internal static void WriteLineConfig(BinaryWriter w, LineConfig v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.ConfigVersion);
            WriteLineDurations(w, v.Durations);
            w.Write(v.EarlyFinishSeconds);
            w.Write(v.PairingTries);
            w.Write(v.AbandonSeconds);
            w.Write(v.StartPrim);
            w.Write(v.BonusPairShare);
            w.Write(v.BonusPairTaker);
            w.Write(v.BonusPairSharer);
            w.Write(v.BonusPairBothTake);
            w.Write(v.BonusTrioNone);
            w.Write(v.BonusTrioTaker);
            w.Write(v.BonusTrioOthers);
            w.Write(v.BonusTrioMany);
            w.Write(v.CashTake);
            w.Write(v.CashCaughtBlame);
            w.Write(v.CashWhistleBlame);
            w.Write(v.CashWhistleReward);
            w.Write(v.ShiftSkipPay);
            w.Write(v.InvestigationsPerPlayer);
            w.Write(v.InvestigationCost);
            w.Write(v.InvestigationGuiltyBlame);
            w.Write(v.InvestigationRefund);
            w.Write(v.InvestigationFalseBlame);
            w.Write(v.BlamePerVote);
            w.Write(v.CrisisBlame);
            WriteList(w, v.CandidatesByPlayers, (b, x) => { b.Write(x); });
            WriteList(w, v.FiringsByPlayers, (b, x) => { b.Write(x); });
            w.Write(v.TransferMax);
            w.Write(v.TransferMinInterval);
            w.Write(v.RecordingsEnabled);
            w.Write(v.RecorderUses);
            w.Write(v.LeakSeconds);
            w.Write(v.LeakDelay);
            w.Write(v.TextMaxChars);
            w.Write(v.MeetingChatMaxChars);
            w.Write(v.MessageMinInterval);
            w.Write(v.TextsPerSegment);
            w.Write(v.MeetingChatPerSegment);
            w.Write(v.MessagesKeptPerPlayer);
        }
        internal static LineConfig ReadLineConfig(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineConfig();
            v.ConfigVersion = r.ReadInt32();
            v.Durations = ReadLineDurations(r);
            v.EarlyFinishSeconds = r.ReadSingle();
            v.PairingTries = r.ReadInt32();
            v.AbandonSeconds = r.ReadSingle();
            v.StartPrim = r.ReadInt32();
            v.BonusPairShare = r.ReadInt32();
            v.BonusPairTaker = r.ReadInt32();
            v.BonusPairSharer = r.ReadInt32();
            v.BonusPairBothTake = r.ReadInt32();
            v.BonusTrioNone = r.ReadInt32();
            v.BonusTrioTaker = r.ReadInt32();
            v.BonusTrioOthers = r.ReadInt32();
            v.BonusTrioMany = r.ReadInt32();
            v.CashTake = r.ReadInt32();
            v.CashCaughtBlame = r.ReadInt32();
            v.CashWhistleBlame = r.ReadInt32();
            v.CashWhistleReward = r.ReadInt32();
            v.ShiftSkipPay = r.ReadInt32();
            v.InvestigationsPerPlayer = r.ReadInt32();
            v.InvestigationCost = r.ReadInt32();
            v.InvestigationGuiltyBlame = r.ReadInt32();
            v.InvestigationRefund = r.ReadInt32();
            v.InvestigationFalseBlame = r.ReadInt32();
            v.BlamePerVote = r.ReadInt32();
            v.CrisisBlame = r.ReadInt32();
            v.CandidatesByPlayers = ReadList(r, b => b.ReadInt32());
            v.FiringsByPlayers = ReadList(r, b => b.ReadInt32());
            v.TransferMax = r.ReadInt32();
            v.TransferMinInterval = r.ReadSingle();
            v.RecordingsEnabled = ReadBool(r);
            v.RecorderUses = r.ReadInt32();
            v.LeakSeconds = r.ReadSingle();
            v.LeakDelay = r.ReadSingle();
            v.TextMaxChars = r.ReadInt32();
            v.MeetingChatMaxChars = r.ReadInt32();
            v.MessageMinInterval = r.ReadSingle();
            v.TextsPerSegment = r.ReadInt32();
            v.MeetingChatPerSegment = r.ReadInt32();
            v.MessagesKeptPerPlayer = r.ReadInt32();
            return v;
        }

        internal static void WriteLineView(BinaryWriter w, LineView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.WeekId);
            WriteString(w, v.MatchId);
            WriteString(w, v.RulesetVersion);
            w.Write(v.ProtocolVersion);
            w.Write(v.StateVersion);
            w.Write(v.Me);
            w.Write(v.Now);
            WriteLinePhase(w, v.Phase);
            w.Write(v.PhaseStartTime);
            w.Write(v.PhaseEndTime);
            w.Write(v.Day);
            w.Write(v.TaskNumber);
            WriteLineFeaturesView(w, v.Features);
            WriteList(w, v.Players, (b, x) => { WriteLinePlayerView(b, x); });
            WriteList(w, v.LineBoard, (b, x) => { WriteLineBoardCall(b, x); });
            WriteLineMeView(w, v.Mine);
            WriteLineTaskView(w, v.Task);
            WriteLineMeetingView(w, v.Meeting);
            WriteLineFinalView(w, v.Final);
            WriteLineWeekResult(w, v.Results);
        }
        internal static LineView ReadLineView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineView();
            v.WeekId = r.ReadInt32();
            v.MatchId = ReadString(r);
            v.RulesetVersion = ReadString(r);
            v.ProtocolVersion = r.ReadInt32();
            v.StateVersion = r.ReadInt64();
            v.Me = r.ReadInt32();
            v.Now = r.ReadDouble();
            v.Phase = ReadLinePhase(r);
            v.PhaseStartTime = r.ReadDouble();
            v.PhaseEndTime = r.ReadDouble();
            v.Day = r.ReadInt32();
            v.TaskNumber = r.ReadInt32();
            v.Features = ReadLineFeaturesView(r);
            v.Players = ReadList(r, b => ReadLinePlayerView(b));
            v.LineBoard = ReadList(r, b => ReadLineBoardCall(b));
            v.Mine = ReadLineMeView(r);
            v.Task = ReadLineTaskView(r);
            v.Meeting = ReadLineMeetingView(r);
            v.Final = ReadLineFinalView(r);
            v.Results = ReadLineWeekResult(r);
            return v;
        }

        internal static void WriteLineFeaturesView(BinaryWriter w, LineFeaturesView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.RecordingsEnabled);
            w.Write(v.CallsOpen);
            w.Write(v.OpenVoice);
            w.Write(v.TransfersOpen);
            w.Write(v.TextsOpen);
            w.Write(v.MeetingChatOpen);
            w.Write(v.WhistleOpen);
            w.Write(v.InvestigationOpen);
            w.Write(v.LeakOpen);
            w.Write(v.VoteOpen);
        }
        internal static LineFeaturesView ReadLineFeaturesView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineFeaturesView();
            v.RecordingsEnabled = ReadBool(r);
            v.CallsOpen = ReadBool(r);
            v.OpenVoice = ReadBool(r);
            v.TransfersOpen = ReadBool(r);
            v.TextsOpen = ReadBool(r);
            v.MeetingChatOpen = ReadBool(r);
            v.WhistleOpen = ReadBool(r);
            v.InvestigationOpen = ReadBool(r);
            v.LeakOpen = ReadBool(r);
            v.VoteOpen = ReadBool(r);
            return v;
        }

        internal static void WriteLinePlayerView(BinaryWriter w, LinePlayerView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Id);
            WriteString(w, v.Name);
            w.Write(v.ColorIndex);
            w.Write(v.Connected);
            w.Write(v.IsBot);
            w.Write(v.Blame);
            w.Write(v.Submitted);
            w.Write(v.Voted);
            w.Write(v.Candidate);
            w.Write(v.Fired);
        }
        internal static LinePlayerView ReadLinePlayerView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LinePlayerView();
            v.Id = r.ReadInt32();
            v.Name = ReadString(r);
            v.ColorIndex = r.ReadInt32();
            v.Connected = ReadBool(r);
            v.IsBot = ReadBool(r);
            v.Blame = r.ReadInt32();
            v.Submitted = ReadBool(r);
            v.Voted = ReadBool(r);
            v.Candidate = ReadBool(r);
            v.Fired = ReadBool(r);
            return v;
        }

        internal static void WriteLineBoardCall(BinaryWriter w, LineBoardCall v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.CallId);
            w.Write(v.Caller);
            w.Write(v.Callee);
            WriteCallStatus(w, v.Status);
            w.Write(v.Start);
        }
        internal static LineBoardCall ReadLineBoardCall(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineBoardCall();
            v.CallId = r.ReadInt32();
            v.Caller = r.ReadInt32();
            v.Callee = r.ReadInt32();
            v.Status = ReadCallStatus(r);
            v.Start = r.ReadDouble();
            return v;
        }

        internal static void WriteLineMeView(BinaryWriter w, LineMeView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Prim);
            WriteLinePhoneView(w, v.Phone);
            WriteList(w, v.CallLog, (b, x) => { WriteLineCallRecordView(b, x); });
            WriteList(w, v.Transfers, (b, x) => { WriteLineTransferView(b, x); });
            w.Write(v.RecorderUsesLeft);
            WriteList(w, v.Recorded, (b, x) => { WriteLineRecordedCallView(b, x); });
            w.Write(v.InvestigationUsed);
            WriteList(w, v.Ballots, (b, x) => { b.Write(x); });
            WriteList(w, v.Group, (b, x) => { b.Write(x); });
            WriteList(w, v.WitnessOf, (b, x) => { b.Write(x); });
            WriteList(w, v.InvestigationTargets, (b, x) => { b.Write(x); });
            w.Write(v.MessageSeq);
            w.Write(v.TextsLeft);
            w.Write(v.MeetingChatLeft);
            w.Write(v.NextMessageAt);
            w.Write(v.NextTransferAt);
        }
        internal static LineMeView ReadLineMeView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineMeView();
            v.Prim = r.ReadInt32();
            v.Phone = ReadLinePhoneView(r);
            v.CallLog = ReadList(r, b => ReadLineCallRecordView(b));
            v.Transfers = ReadList(r, b => ReadLineTransferView(b));
            v.RecorderUsesLeft = r.ReadInt32();
            v.Recorded = ReadList(r, b => ReadLineRecordedCallView(b));
            v.InvestigationUsed = ReadBool(r);
            v.Ballots = ReadList(r, b => b.ReadInt32());
            v.Group = ReadList(r, b => b.ReadInt32());
            v.WitnessOf = ReadList(r, b => b.ReadInt32());
            v.InvestigationTargets = ReadList(r, b => b.ReadInt32());
            v.MessageSeq = r.ReadInt64();
            v.TextsLeft = r.ReadInt32();
            v.MeetingChatLeft = r.ReadInt32();
            v.NextMessageAt = r.ReadDouble();
            v.NextTransferAt = r.ReadDouble();
            return v;
        }

        internal static void WriteLinePhoneView(BinaryWriter w, LinePhoneView v)
        {
            w.Write(v != null); if (v == null) return;
            WriteCallStatus(w, v.Status);
            w.Write(v.Other);
            w.Write(v.CallId);
            w.Write(v.Since);
            w.Write(v.RingUntil);
        }
        internal static LinePhoneView ReadLinePhoneView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LinePhoneView();
            v.Status = ReadCallStatus(r);
            v.Other = r.ReadInt32();
            v.CallId = r.ReadInt32();
            v.Since = r.ReadDouble();
            v.RingUntil = r.ReadDouble();
            return v;
        }

        internal static void WriteLineCallRecordView(BinaryWriter w, LineCallRecordView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.CallId);
            w.Write(v.Other);
            w.Write(v.Outgoing);
            w.Write(v.Connected);
            w.Write(v.Start);
            w.Write(v.End);
            WriteCallEndReason(w, v.EndReason);
        }
        internal static LineCallRecordView ReadLineCallRecordView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineCallRecordView();
            v.CallId = r.ReadInt32();
            v.Other = r.ReadInt32();
            v.Outgoing = ReadBool(r);
            v.Connected = ReadBool(r);
            v.Start = r.ReadDouble();
            v.End = r.ReadDouble();
            v.EndReason = ReadCallEndReason(r);
            return v;
        }

        internal static void WriteLineTransferView(BinaryWriter w, LineTransferView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.From);
            w.Write(v.To);
            w.Write(v.Amount);
            w.Write(v.Time);
            WriteLinePhase(w, v.Phase);
        }
        internal static LineTransferView ReadLineTransferView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineTransferView();
            v.From = r.ReadInt32();
            v.To = r.ReadInt32();
            v.Amount = r.ReadInt32();
            v.Time = r.ReadDouble();
            v.Phase = ReadLinePhase(r);
            return v;
        }

        internal static void WriteLineRecordedCallView(BinaryWriter w, LineRecordedCallView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.CallId);
            w.Write(v.Other);
            w.Write(v.Start);
            w.Write(v.Leaked);
        }
        internal static LineRecordedCallView ReadLineRecordedCallView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineRecordedCallView();
            v.CallId = r.ReadInt32();
            v.Other = r.ReadInt32();
            v.Start = r.ReadDouble();
            v.Leaked = ReadBool(r);
            return v;
        }

        internal static void WriteLineTaskView(BinaryWriter w, LineTaskView v)
        {
            w.Write(v != null); if (v == null) return;
            WriteTaskKind(w, v.Kind);
            w.Write(v.Number);
            WriteTaskFamily(w, v.Family);
            w.Write(v.PlayEndTime);
            WriteList(w, v.Group, (b, x) => { b.Write(x); });
            w.Write(v.Needed);
            w.Write(v.Submitted);
            w.Write(v.MyChoice);
            w.Write(v.Revealed);
            WriteList(w, v.Choices, (b, x) => { WriteLineChoiceView(b, x); });
            w.Write(v.MyPayout);
            w.Write(v.Takers);
            WriteList(w, v.Roster, (b, x) => { b.Write(x); });
        }
        internal static LineTaskView ReadLineTaskView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineTaskView();
            v.Kind = ReadTaskKind(r);
            v.Number = r.ReadInt32();
            v.Family = ReadTaskFamily(r);
            v.PlayEndTime = r.ReadDouble();
            v.Group = ReadList(r, b => b.ReadInt32());
            v.Needed = r.ReadInt32();
            v.Submitted = ReadBool(r);
            v.MyChoice = r.ReadInt32();
            v.Revealed = ReadBool(r);
            v.Choices = ReadList(r, b => ReadLineChoiceView(b));
            v.MyPayout = r.ReadInt32();
            v.Takers = r.ReadInt32();
            v.Roster = ReadList(r, b => b.ReadInt32());
            return v;
        }

        internal static void WriteLineChoiceView(BinaryWriter w, LineChoiceView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.TaskNumber);
            WriteTaskKind(w, v.Task);
            w.Write(v.Player);
            w.Write(v.Choice);
            w.Write(v.Payout);
        }
        internal static LineChoiceView ReadLineChoiceView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineChoiceView();
            v.TaskNumber = r.ReadInt32();
            v.Task = ReadTaskKind(r);
            v.Player = r.ReadInt32();
            v.Choice = r.ReadInt32();
            v.Payout = r.ReadInt32();
            return v;
        }

        internal static void WriteLineMeetingView(BinaryWriter w, LineMeetingView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Index);
            WriteList(w, v.FailedTasks, (b, x) => { WriteTaskKind(b, x); });
            w.Write(v.VoteableCount);
            WriteList(w, v.Dossier, (b, x) => { WriteLineDossierEntry(b, x); });
            WriteList(w, v.Roster, (b, x) => { b.Write(x); });
            WriteList(w, v.Recordings, (b, x) => { WriteLineRecordingPick(b, x); });
            WriteList(w, v.BlameGained, (b, x) => { WriteLineBlameGainView(b, x); });
        }
        internal static LineMeetingView ReadLineMeetingView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineMeetingView();
            v.Index = r.ReadInt32();
            v.FailedTasks = ReadList(r, b => ReadTaskKind(b));
            v.VoteableCount = r.ReadInt32();
            v.Dossier = ReadList(r, b => ReadLineDossierEntry(b));
            v.Roster = ReadList(r, b => b.ReadInt32());
            v.Recordings = ReadList(r, b => ReadLineRecordingPick(b));
            v.BlameGained = ReadList(r, b => ReadLineBlameGainView(b));
            return v;
        }

        internal static void WriteLineDossierEntry(BinaryWriter w, LineDossierEntry v)
        {
            w.Write(v != null); if (v == null) return;
            WriteDossierKind(w, v.Kind);
            WriteTaskKind(w, v.Task);
            w.Write(v.Player);
            w.Write(v.Other);
            w.Write(v.N1);
            w.Write(v.N2);
            w.Write(v.Time);
        }
        internal static LineDossierEntry ReadLineDossierEntry(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineDossierEntry();
            v.Kind = ReadDossierKind(r);
            v.Task = ReadTaskKind(r);
            v.Player = r.ReadInt32();
            v.Other = r.ReadInt32();
            v.N1 = r.ReadInt32();
            v.N2 = r.ReadInt32();
            v.Time = r.ReadDouble();
            return v;
        }

        internal static void WriteLineBlameGainView(BinaryWriter w, LineBlameGainView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Player);
            w.Write(v.Amount);
        }
        internal static LineBlameGainView ReadLineBlameGainView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineBlameGainView();
            v.Player = r.ReadInt32();
            v.Amount = r.ReadInt32();
            return v;
        }

        internal static void WriteLineRecordingPick(BinaryWriter w, LineRecordingPick v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Index);
            WriteList(w, v.CandidateCallIds, (b, x) => { b.Write(x); });
            w.Write(v.PlayAt);
            w.Write(v.Seconds);
            w.Write(v.ChosenCallId);
            w.Write(v.Caller);
            w.Write(v.Callee);
            w.Write(v.CallStart);
            w.Write(v.Leaker);
            w.Write(v.Played);
            w.Write(v.Empty);
        }
        internal static LineRecordingPick ReadLineRecordingPick(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineRecordingPick();
            v.Index = r.ReadInt32();
            v.CandidateCallIds = ReadList(r, b => b.ReadInt32());
            v.PlayAt = r.ReadDouble();
            v.Seconds = r.ReadSingle();
            v.ChosenCallId = r.ReadInt32();
            v.Caller = r.ReadInt32();
            v.Callee = r.ReadInt32();
            v.CallStart = r.ReadDouble();
            v.Leaker = r.ReadInt32();
            v.Played = ReadBool(r);
            v.Empty = ReadBool(r);
            return v;
        }

        internal static void WriteLineFinalView(BinaryWriter w, LineFinalView v)
        {
            w.Write(v != null); if (v == null) return;
            WriteList(w, v.Crisis, (b, x) => { WriteLineBlameGainView(b, x); });
            WriteList(w, v.Candidates, (b, x) => { b.Write(x); });
            WriteList(w, v.DefenseOrder, (b, x) => { b.Write(x); });
            w.Write(v.Defender);
            w.Write(v.DefenderEndTime);
            w.Write(v.Round);
            w.Write(v.Revote);
            WriteList(w, v.Options, (b, x) => { b.Write(x); });
            w.Write(v.FiringsTotal);
            WriteList(w, v.Firings, (b, x) => { WriteLineFiringView(b, x); });
        }
        internal static LineFinalView ReadLineFinalView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineFinalView();
            v.Crisis = ReadList(r, b => ReadLineBlameGainView(b));
            v.Candidates = ReadList(r, b => b.ReadInt32());
            v.DefenseOrder = ReadList(r, b => b.ReadInt32());
            v.Defender = r.ReadInt32();
            v.DefenderEndTime = r.ReadDouble();
            v.Round = r.ReadInt32();
            v.Revote = ReadBool(r);
            v.Options = ReadList(r, b => b.ReadInt32());
            v.FiringsTotal = r.ReadInt32();
            v.Firings = ReadList(r, b => ReadLineFiringView(b));
            return v;
        }

        internal static void WriteLineFiringView(BinaryWriter w, LineFiringView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Player);
            w.Write(v.Round);
            WriteFiringDecision(w, v.Decision);
            w.Write(v.Votes);
            w.Write(v.Desk);
            WriteList(w, v.Recipients, (b, x) => { b.Write(x); });
            w.Write(v.Share);
            w.Write(v.Removed);
        }
        internal static LineFiringView ReadLineFiringView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineFiringView();
            v.Player = r.ReadInt32();
            v.Round = r.ReadInt32();
            v.Decision = ReadFiringDecision(r);
            v.Votes = r.ReadInt32();
            v.Desk = r.ReadInt32();
            v.Recipients = ReadList(r, b => b.ReadInt32());
            v.Share = r.ReadInt32();
            v.Removed = r.ReadInt32();
            return v;
        }

        internal static void WriteLineMessage(BinaryWriter w, LineMessage v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Seq);
            w.Write(v.Id);
            WriteMessageKind(w, v.Kind);
            w.Write(v.From);
            w.Write(v.To);
            w.Write(v.ToAll);
            WriteString(w, v.Text);
            w.Write(v.Time);
        }
        internal static LineMessage ReadLineMessage(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineMessage();
            v.Seq = r.ReadInt64();
            v.Id = r.ReadInt32();
            v.Kind = ReadMessageKind(r);
            v.From = r.ReadInt32();
            v.To = r.ReadInt32();
            v.ToAll = ReadBool(r);
            v.Text = ReadString(r);
            v.Time = r.ReadDouble();
            return v;
        }

        internal static void WriteLineWeekResult(BinaryWriter w, LineWeekResult v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.WeekId);
            WriteString(w, v.MatchId);
            WriteString(w, v.RulesetVersion);
            w.Write(v.PlayerCount);
            w.Write(v.StartTime);
            w.Write(v.EndTime);
            WriteList(w, v.Players, (b, x) => { WriteLineWeekPlayerResult(b, x); });
            WriteList(w, v.Fired, (b, x) => { b.Write(x); });
            WriteList(w, v.Winners, (b, x) => { b.Write(x); });
            WriteList(w, v.Firings, (b, x) => { WriteLineFiringView(b, x); });
            WriteList(w, v.Ballots, (b, x) => { WriteLineBallotView(b, x); });
            WriteList(w, v.Transfers, (b, x) => { WriteLineTransferView(b, x); });
            WriteList(w, v.Choices, (b, x) => { WriteLineChoiceView(b, x); });
            WriteList(w, v.Incidents, (b, x) => { WriteLineDossierEntry(b, x); });
            WriteList(w, v.PrimLog, (b, x) => { WriteLinePrimChange(b, x); });
            WriteList(w, v.BlameLog, (b, x) => { WriteLineBlameChange(b, x); });
            w.Write(v.ResultHash);
        }
        internal static LineWeekResult ReadLineWeekResult(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineWeekResult();
            v.WeekId = r.ReadInt32();
            v.MatchId = ReadString(r);
            v.RulesetVersion = ReadString(r);
            v.PlayerCount = r.ReadInt32();
            v.StartTime = r.ReadDouble();
            v.EndTime = r.ReadDouble();
            v.Players = ReadList(r, b => ReadLineWeekPlayerResult(b));
            v.Fired = ReadList(r, b => b.ReadInt32());
            v.Winners = ReadList(r, b => b.ReadInt32());
            v.Firings = ReadList(r, b => ReadLineFiringView(b));
            v.Ballots = ReadList(r, b => ReadLineBallotView(b));
            v.Transfers = ReadList(r, b => ReadLineTransferView(b));
            v.Choices = ReadList(r, b => ReadLineChoiceView(b));
            v.Incidents = ReadList(r, b => ReadLineDossierEntry(b));
            v.PrimLog = ReadList(r, b => ReadLinePrimChange(b));
            v.BlameLog = ReadList(r, b => ReadLineBlameChange(b));
            v.ResultHash = r.ReadUInt64();
            return v;
        }

        internal static void WriteLineWeekPlayerResult(BinaryWriter w, LineWeekPlayerResult v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Player);
            w.Write(v.Prim);
            w.Write(v.Blame);
            w.Write(v.Fired);
            w.Write(v.Winner);
            w.Write(v.WasCandidate);
            w.Write(v.Recorded);
            w.Write(v.Leaked);
        }
        internal static LineWeekPlayerResult ReadLineWeekPlayerResult(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineWeekPlayerResult();
            v.Player = r.ReadInt32();
            v.Prim = r.ReadInt32();
            v.Blame = r.ReadInt32();
            v.Fired = ReadBool(r);
            v.Winner = ReadBool(r);
            v.WasCandidate = ReadBool(r);
            v.Recorded = r.ReadInt32();
            v.Leaked = r.ReadInt32();
            return v;
        }

        internal static void WriteLineBallotView(BinaryWriter w, LineBallotView v)
        {
            w.Write(v != null); if (v == null) return;
            WriteBallotKind(w, v.Kind);
            w.Write(v.Round);
            w.Write(v.Revote);
            w.Write(v.Voter);
            w.Write(v.Index);
            w.Write(v.Target);
            w.Write(v.SelfAssigned);
        }
        internal static LineBallotView ReadLineBallotView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineBallotView();
            v.Kind = ReadBallotKind(r);
            v.Round = r.ReadInt32();
            v.Revote = ReadBool(r);
            v.Voter = r.ReadInt32();
            v.Index = r.ReadInt32();
            v.Target = r.ReadInt32();
            v.SelfAssigned = ReadBool(r);
            return v;
        }

        internal static void WriteLinePrimChange(BinaryWriter w, LinePrimChange v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Player);
            w.Write(v.Delta);
            WritePrimReason(w, v.Reason);
            w.Write(v.Other);
            w.Write(v.Time);
        }
        internal static LinePrimChange ReadLinePrimChange(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LinePrimChange();
            v.Player = r.ReadInt32();
            v.Delta = r.ReadInt32();
            v.Reason = ReadPrimReason(r);
            v.Other = r.ReadInt32();
            v.Time = r.ReadDouble();
            return v;
        }

        internal static void WriteLineBlameChange(BinaryWriter w, LineBlameChange v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Player);
            w.Write(v.Delta);
            WriteBlameReason(w, v.Reason);
            w.Write(v.Time);
        }
        internal static LineBlameChange ReadLineBlameChange(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineBlameChange();
            v.Player = r.ReadInt32();
            v.Delta = r.ReadInt32();
            v.Reason = ReadBlameReason(r);
            v.Time = r.ReadDouble();
            return v;
        }
    }
}
