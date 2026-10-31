using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// Explicit v4 little-endian wire schema. The header is magic, UInt16 protocol, message kind.
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
                if (stream.Length > MaxPayload) throw new FormatException("v4 payload too large.");
                return stream.ToArray();
            }
        }

        private static T Decode<T>(byte[] payload, byte kind, Func<BinaryReader, T> read)
        {
            if (payload == null || payload.Length > MaxPayload) throw new FormatException("Invalid v4 payload.");
            try
            {
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadByte() != Magic || reader.ReadUInt16() != LineProtocol.Version || reader.ReadByte() != kind)
                        throw new FormatException("Incompatible v4 message.");
                    T value = read(reader);
                    if (stream.Position != stream.Length) throw new FormatException("Trailing v4 payload bytes.");
                    return value;
                }
            }
            catch (IOException ex) { throw new FormatException("Truncated v4 payload.", ex); }
            catch (ArgumentException ex) { throw new FormatException("Malformed v4 payload.", ex); }
            catch (OverflowException ex) { throw new FormatException("Invalid v4 size.", ex); }
        }

        private static bool ReadBool(BinaryReader r)
        {
            byte b = r.ReadByte();
            if (b > 1) throw new FormatException("Invalid boolean.");
            return b == 1;
        }

        private static byte ReadEnum(BinaryReader r, byte max)
        {
            byte b = r.ReadByte();
            if (b > max) throw new FormatException("Invalid enum.");
            return b;
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

        // v4 schema: declaration order, explicit primitive widths; no reflection at runtime.
        private static LinePhase ReadLinePhase(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14: return (LinePhase)x; default: throw new FormatException("Unknown LinePhase."); } }
        private static void WriteLinePhase(BinaryWriter w, LinePhase v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14: w.Write((byte)v); break; default: throw new FormatException("Unknown LinePhase."); } }
        private static PhaseEndReason ReadPhaseEndReason(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: return (PhaseEndReason)x; default: throw new FormatException("Unknown PhaseEndReason."); } }
        private static void WritePhaseEndReason(BinaryWriter w, PhaseEndReason v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: w.Write((byte)v); break; default: throw new FormatException("Unknown PhaseEndReason."); } }
        private static WeekLength ReadWeekLength(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: return (WeekLength)x; default: throw new FormatException("Unknown WeekLength."); } }
        private static void WriteWeekLength(BinaryWriter w, WeekLength v)
        { switch ((byte)v) { case 0: case 1: w.Write((byte)v); break; default: throw new FormatException("Unknown WeekLength."); } }
        private static TaskKind ReadTaskKind(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: return (TaskKind)x; default: throw new FormatException("Unknown TaskKind."); } }
        private static void WriteTaskKind(BinaryWriter w, TaskKind v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: w.Write((byte)v); break; default: throw new FormatException("Unknown TaskKind."); } }
        private static TaskFamily ReadTaskFamily(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: return (TaskFamily)x; default: throw new FormatException("Unknown TaskFamily."); } }
        private static void WriteTaskFamily(BinaryWriter w, TaskFamily v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: w.Write((byte)v); break; default: throw new FormatException("Unknown TaskFamily."); } }
        private static TrickType ReadTrickType(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: return (TrickType)x; default: throw new FormatException("Unknown TrickType."); } }
        private static void WriteTrickType(BinaryWriter w, TrickType v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: w.Write((byte)v); break; default: throw new FormatException("Unknown TrickType."); } }
        private static CallStatus ReadCallStatus(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: return (CallStatus)x; default: throw new FormatException("Unknown CallStatus."); } }
        private static void WriteCallStatus(BinaryWriter w, CallStatus v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: w.Write((byte)v); break; default: throw new FormatException("Unknown CallStatus."); } }
        private static CallEndReason ReadCallEndReason(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: return (CallEndReason)x; default: throw new FormatException("Unknown CallEndReason."); } }
        private static void WriteCallEndReason(BinaryWriter w, CallEndReason v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: w.Write((byte)v); break; default: throw new FormatException("Unknown CallEndReason."); } }
        private static LineVerb ReadLineVerb(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14: case 15: case 16: case 17: case 18: case 19: return (LineVerb)x; default: throw new FormatException("Unknown LineVerb."); } }
        private static void WriteLineVerb(BinaryWriter w, LineVerb v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14: case 15: case 16: case 17: case 18: case 19: w.Write((byte)v); break; default: throw new FormatException("Unknown LineVerb."); } }
        private static TaskField ReadTaskField(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: return (TaskField)x; default: throw new FormatException("Unknown TaskField."); } }
        private static void WriteTaskField(BinaryWriter w, TaskField v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: w.Write((byte)v); break; default: throw new FormatException("Unknown TaskField."); } }
        private static LineFailure ReadLineFailure(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14: case 15: case 16: case 17: case 18: case 19: case 20: case 21: case 22: case 23: case 24: case 25: case 26: case 27: return (LineFailure)x; default: throw new FormatException("Unknown LineFailure."); } }
        private static void WriteLineFailure(BinaryWriter w, LineFailure v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14: case 15: case 16: case 17: case 18: case 19: case 20: case 21: case 22: case 23: case 24: case 25: case 26: case 27: w.Write((byte)v); break; default: throw new FormatException("Unknown LineFailure."); } }
        private static LineAudience ReadLineAudience(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: return (LineAudience)x; default: throw new FormatException("Unknown LineAudience."); } }
        private static void WriteLineAudience(BinaryWriter w, LineAudience v)
        { switch ((byte)v) { case 0: case 1: case 2: w.Write((byte)v); break; default: throw new FormatException("Unknown LineAudience."); } }
        private static LineEventType ReadLineEventType(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14: case 15: case 16: case 17: case 18: case 19: case 20: case 21: case 22: case 23: case 24: case 25: case 26: case 27: case 28: case 29: case 30: case 31: case 32: case 40: case 41: case 42: case 43: case 44: case 45: case 46: case 47: case 48: return (LineEventType)x; default: throw new FormatException("Unknown LineEventType."); } }
        private static void WriteLineEventType(BinaryWriter w, LineEventType v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14: case 15: case 16: case 17: case 18: case 19: case 20: case 21: case 22: case 23: case 24: case 25: case 26: case 27: case 28: case 29: case 30: case 31: case 32: case 40: case 41: case 42: case 43: case 44: case 45: case 46: case 47: case 48: w.Write((byte)v); break; default: throw new FormatException("Unknown LineEventType."); } }
        private static BlameReason ReadBlameReason(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: return (BlameReason)x; default: throw new FormatException("Unknown BlameReason."); } }
        private static void WriteBlameReason(BinaryWriter w, BlameReason v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: w.Write((byte)v); break; default: throw new FormatException("Unknown BlameReason."); } }
        private static MoneyReason ReadMoneyReason(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: return (MoneyReason)x; default: throw new FormatException("Unknown MoneyReason."); } }
        private static void WriteMoneyReason(BinaryWriter w, MoneyReason v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: w.Write((byte)v); break; default: throw new FormatException("Unknown MoneyReason."); } }
        private static MessageKind ReadMessageKind(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: return (MessageKind)x; default: throw new FormatException("Unknown MessageKind."); } }
        private static void WriteMessageKind(BinaryWriter w, MessageKind v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: w.Write((byte)v); break; default: throw new FormatException("Unknown MessageKind."); } }
        private static RecordSource ReadRecordSource(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: return (RecordSource)x; default: throw new FormatException("Unknown RecordSource."); } }
        private static void WriteRecordSource(BinaryWriter w, RecordSource v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: w.Write((byte)v); break; default: throw new FormatException("Unknown RecordSource."); } }
        private static RecordKind ReadRecordKind(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14: case 15: case 16: return (RecordKind)x; default: throw new FormatException("Unknown RecordKind."); } }
        private static void WriteRecordKind(BinaryWriter w, RecordKind v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14: case 15: case 16: w.Write((byte)v); break; default: throw new FormatException("Unknown RecordKind."); } }
        private static OfferKind ReadOfferKind(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: return (OfferKind)x; default: throw new FormatException("Unknown OfferKind."); } }
        private static void WriteOfferKind(BinaryWriter w, OfferKind v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: w.Write((byte)v); break; default: throw new FormatException("Unknown OfferKind."); } }
        private static IncidentKind ReadIncidentKind(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: return (IncidentKind)x; default: throw new FormatException("Unknown IncidentKind."); } }
        private static void WriteIncidentKind(BinaryWriter w, IncidentKind v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11: w.Write((byte)v); break; default: throw new FormatException("Unknown IncidentKind."); } }
        private static ObjectiveKind ReadObjectiveKind(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: return (ObjectiveKind)x; default: throw new FormatException("Unknown ObjectiveKind."); } }
        private static void WriteObjectiveKind(BinaryWriter w, ObjectiveKind v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: w.Write((byte)v); break; default: throw new FormatException("Unknown ObjectiveKind."); } }
        private static CareerItem ReadCareerItem(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: return (CareerItem)x; default: throw new FormatException("Unknown CareerItem."); } }
        private static void WriteCareerItem(BinaryWriter w, CareerItem v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: w.Write((byte)v); break; default: throw new FormatException("Unknown CareerItem."); } }
        private static ControlKind ReadControlKind(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: return (ControlKind)x; default: throw new FormatException("Unknown ControlKind."); } }
        private static void WriteControlKind(BinaryWriter w, ControlKind v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: w.Write((byte)v); break; default: throw new FormatException("Unknown ControlKind."); } }
        private static DossierKind ReadDossierKind(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: return (DossierKind)x; default: throw new FormatException("Unknown DossierKind."); } }
        private static void WriteDossierKind(BinaryWriter w, DossierKind v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: w.Write((byte)v); break; default: throw new FormatException("Unknown DossierKind."); } }
        private static RecordingSelection ReadRecordingSelection(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: return (RecordingSelection)x; default: throw new FormatException("Unknown RecordingSelection."); } }
        private static void WriteRecordingSelection(BinaryWriter w, RecordingSelection v)
        { switch ((byte)v) { case 0: case 1: w.Write((byte)v); break; default: throw new FormatException("Unknown RecordingSelection."); } }
        private static EarlySubmitPolicy ReadEarlySubmitPolicy(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: return (EarlySubmitPolicy)x; default: throw new FormatException("Unknown EarlySubmitPolicy."); } }
        private static void WriteEarlySubmitPolicy(BinaryWriter w, EarlySubmitPolicy v)
        { switch ((byte)v) { case 0: case 1: w.Write((byte)v); break; default: throw new FormatException("Unknown EarlySubmitPolicy."); } }
        private static VoteOutcome ReadVoteOutcome(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: return (VoteOutcome)x; default: throw new FormatException("Unknown VoteOutcome."); } }
        private static void WriteVoteOutcome(BinaryWriter w, VoteOutcome v)
        { switch ((byte)v) { case 0: case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: w.Write((byte)v); break; default: throw new FormatException("Unknown VoteOutcome."); } }
        private static CompanyChange ReadCompanyChange(BinaryReader r)
        { byte x = r.ReadByte(); switch (x) { case 0: case 1: case 2: return (CompanyChange)x; default: throw new FormatException("Unknown CompanyChange."); } }
        private static void WriteCompanyChange(BinaryWriter w, CompanyChange v)
        { switch ((byte)v) { case 0: case 1: case 2: w.Write((byte)v); break; default: throw new FormatException("Unknown CompanyChange."); } }
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
            WriteWeekLength(w, v.Length);
            WriteList(w, v.Players, (b, x) => { WriteLinePlayerInfo(b, x); });
            WriteString(w, v.ManagerProfileId);
            w.Write(v.RecordingsEnabled);
            WriteLineSessionHistory(w, v.History);
            WriteList(w, v.ForceObjectives, (b, x) => { WriteObjectiveKind(b, x); });
            WriteList(w, v.ForceObjectiveTargets, (b, x) => { b.Write(x); });
            WriteList(w, v.ForceTricks, (b, x) => { WriteTrickType(b, x); });
            WriteList(w, v.ForceTasks, (b, x) => { WriteTaskKind(b, x); });
            WriteList(w, v.ForceRoles, (b, x) => { b.Write(x); });
        }
        internal static LineSetup ReadLineSetup(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineSetup();
            v.WeekId = r.ReadInt32();
            v.MatchId = ReadString(r);
            v.Seed = r.ReadInt32();
            v.StartTime = r.ReadDouble();
            v.Length = ReadWeekLength(r);
            v.Players = ReadList(r, b => ReadLinePlayerInfo(b));
            v.ManagerProfileId = ReadString(r);
            v.RecordingsEnabled = ReadBool(r);
            v.History = ReadLineSessionHistory(r);
            v.ForceObjectives = ReadList(r, b => ReadObjectiveKind(b));
            v.ForceObjectiveTargets = ReadList(r, b => r.ReadInt32());
            v.ForceTricks = ReadList(r, b => ReadTrickType(b));
            v.ForceTasks = ReadList(r, b => ReadTaskKind(b));
            v.ForceRoles = ReadList(r, b => r.ReadInt32());
            return v;
        }

        internal static void WriteLineSessionHistory(BinaryWriter w, LineSessionHistory v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.WeeksPlayed);
            WriteList(w, v.SessionPoints, (b, x) => { b.Write(x); });
            WriteList(w, v.PastManagers, (b, x) => { WriteString(b, x); });
            WriteList(w, v.SeenTasks, (b, x) => { WriteTaskKind(b, x); });
            WriteList(w, v.TaskUses, (b, x) => { b.Write(x); });
            WriteList(w, v.TimesFired, (b, x) => { b.Write(x); });
            WriteString(w, v.NextManager);
            WriteList(w, v.RoleUses, (b, x) => { b.Write(x); });
        }
        internal static LineSessionHistory ReadLineSessionHistory(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineSessionHistory();
            v.WeeksPlayed = r.ReadInt32();
            v.SessionPoints = ReadList(r, b => r.ReadInt32());
            v.PastManagers = ReadList(r, b => ReadString(b));
            v.SeenTasks = ReadList(r, b => ReadTaskKind(b));
            v.TaskUses = ReadList(r, b => r.ReadInt32());
            v.TimesFired = ReadList(r, b => r.ReadInt32());
            v.NextManager = ReadString(r);
            v.RoleUses = ReadList(r, b => r.ReadInt32());
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
            WriteWeekLength(w, v.Length);
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
            v.Length = ReadWeekLength(r);
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
            w.Write(v.SessionPoints);
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
            v.SessionPoints = r.ReadInt32();
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
            w.Write(v.Morning);
            w.Write(v.MorningFirst);
            w.Write(v.RoundIntro);
            w.Write(v.RoundIntroFirst);
            w.Write(v.RoundPlay);
            w.Write(v.AllSubmittedRemaining);
            w.Write(v.RoundReveal);
            w.Write(v.Break);
            w.Write(v.BreakClosing);
            w.Write(v.BreakMax);
            w.Write(v.Dossier);
            w.Write(v.DossierPerRecording);
            w.Write(v.Discussion);
            w.Write(v.FinalDiscussion);
            w.Write(v.Vote);
            w.Write(v.Verdict);
            w.Write(v.DefensePerPlayer);
            w.Write(v.FinalVote);
            w.Write(v.FinalVerdict);
            w.Write(v.WeekResults);
            w.Write(v.Ring);
            w.Write(v.RemedialTimeout);
        }
        internal static LineDurations ReadLineDurations(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineDurations();
            v.Morning = r.ReadSingle();
            v.MorningFirst = r.ReadSingle();
            v.RoundIntro = r.ReadSingle();
            v.RoundIntroFirst = r.ReadSingle();
            v.RoundPlay = r.ReadSingle();
            v.AllSubmittedRemaining = r.ReadSingle();
            v.RoundReveal = r.ReadSingle();
            v.Break = r.ReadSingle();
            v.BreakClosing = r.ReadSingle();
            v.BreakMax = r.ReadSingle();
            v.Dossier = r.ReadSingle();
            v.DossierPerRecording = r.ReadSingle();
            v.Discussion = r.ReadSingle();
            v.FinalDiscussion = r.ReadSingle();
            v.Vote = r.ReadSingle();
            v.Verdict = r.ReadSingle();
            v.DefensePerPlayer = r.ReadSingle();
            v.FinalVote = r.ReadSingle();
            v.FinalVerdict = r.ReadSingle();
            v.WeekResults = r.ReadSingle();
            v.Ring = r.ReadSingle();
            v.RemedialTimeout = r.ReadSingle();
            return v;
        }

        internal static void WriteLineTaskConfig(BinaryWriter w, LineTaskConfig v)
        {
            w.Write(v != null); if (v == null) return;
            WriteTaskKind(w, v.Kind);
            w.Write(v.Version);
            w.Write(v.Enabled);
            w.Write(v.MaxOutput);
            w.Write(v.PlayFactor);
            w.Write(v.MinPlayers);
            w.Write(v.MaxPlayers);
            WriteString(w, v.ExclusiveManager);
        }
        internal static LineTaskConfig ReadLineTaskConfig(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineTaskConfig();
            v.Kind = ReadTaskKind(r);
            v.Version = r.ReadInt32();
            v.Enabled = ReadBool(r);
            v.MaxOutput = r.ReadInt32();
            v.PlayFactor = r.ReadInt32();
            v.MinPlayers = r.ReadInt32();
            v.MaxPlayers = r.ReadInt32();
            v.ExclusiveManager = ReadString(r);
            return v;
        }

        internal static void WriteLineTaskRules(BinaryWriter w, LineTaskRules v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.InvoiceCorrectIncome);
            w.Write(v.InvoicePaddingLoss);
            w.Write(v.InvoicePaddingKickback);
            w.Write(v.BudgetSuccessIncome);
            w.Write(v.BudgetSurplusIncomeMax);
            w.Write(v.SpecCorrectIncome);
            w.Write(v.SpecCommission);
            w.Write(v.ReportAccurateApproved);
            w.Write(v.ReportAccurateRejected);
            w.Write(v.ReportPolishedApproved);
            w.Write(v.ReportPolishedRejected);
            w.Write(v.ReportPolishedKickback);
            w.Write(v.ReportAcceptedIncome);
            w.Write(v.ReportCorrectDecisionIncome);
            w.Write(v.RoomSuccessIncome);
            w.Write(v.RoomYieldIncome);
            w.Write(v.ArchiveSnoopLoss);
            w.Write(v.ArchiveServedIncome);
            w.Write(v.ArchiveVisitorIncomePerServed);
            WriteList(w, v.BonusPairOutput, (b, x) => { b.Write(x); });
            WriteList(w, v.BonusTrioOutput, (b, x) => { b.Write(x); });
            w.Write(v.BonusAllShareIncome);
            w.Write(v.BonusSoloTakerIncome);
            w.Write(v.BonusTrioTwoTakersIncome);
            w.Write(v.BonusAllTakeIncome);
            w.Write(v.ServerFailBase);
            w.Write(v.ServerFailPerExact);
            w.Write(v.ServerFailCap);
            w.Write(v.ServerExactIncome);
            w.Write(v.ServerCrackedIncome);
            w.Write(v.ServerOffer);
            w.Write(v.PhishingBlockedOutput);
            w.Write(v.PhishingFailedOutput);
            w.Write(v.PhishingFlagIncome);
            w.Write(v.PhishingKickback);
            w.Write(v.ShiftOvertime);
            w.Write(v.VendorBestIncome);
            w.Write(v.VendorKickback);
            w.Write(v.SwapCompleteIncome);
            w.Write(v.SwapCollectorBonus);
        }
        internal static LineTaskRules ReadLineTaskRules(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineTaskRules();
            v.InvoiceCorrectIncome = r.ReadInt32();
            v.InvoicePaddingLoss = r.ReadInt32();
            v.InvoicePaddingKickback = r.ReadInt32();
            v.BudgetSuccessIncome = r.ReadInt32();
            v.BudgetSurplusIncomeMax = r.ReadInt32();
            v.SpecCorrectIncome = r.ReadInt32();
            v.SpecCommission = r.ReadInt32();
            v.ReportAccurateApproved = r.ReadInt32();
            v.ReportAccurateRejected = r.ReadInt32();
            v.ReportPolishedApproved = r.ReadInt32();
            v.ReportPolishedRejected = r.ReadInt32();
            v.ReportPolishedKickback = r.ReadInt32();
            v.ReportAcceptedIncome = r.ReadInt32();
            v.ReportCorrectDecisionIncome = r.ReadInt32();
            v.RoomSuccessIncome = r.ReadInt32();
            v.RoomYieldIncome = r.ReadInt32();
            v.ArchiveSnoopLoss = r.ReadInt32();
            v.ArchiveServedIncome = r.ReadInt32();
            v.ArchiveVisitorIncomePerServed = r.ReadInt32();
            v.BonusPairOutput = ReadList(r, b => r.ReadInt32());
            v.BonusTrioOutput = ReadList(r, b => r.ReadInt32());
            v.BonusAllShareIncome = r.ReadInt32();
            v.BonusSoloTakerIncome = r.ReadInt32();
            v.BonusTrioTwoTakersIncome = r.ReadInt32();
            v.BonusAllTakeIncome = r.ReadInt32();
            v.ServerFailBase = r.ReadInt32();
            v.ServerFailPerExact = r.ReadInt32();
            v.ServerFailCap = r.ReadInt32();
            v.ServerExactIncome = r.ReadInt32();
            v.ServerCrackedIncome = r.ReadInt32();
            v.ServerOffer = r.ReadInt32();
            v.PhishingBlockedOutput = r.ReadInt32();
            v.PhishingFailedOutput = r.ReadInt32();
            v.PhishingFlagIncome = r.ReadInt32();
            v.PhishingKickback = r.ReadInt32();
            v.ShiftOvertime = r.ReadInt32();
            v.VendorBestIncome = r.ReadInt32();
            v.VendorKickback = r.ReadInt32();
            v.SwapCompleteIncome = r.ReadInt32();
            v.SwapCollectorBonus = r.ReadInt32();
            return v;
        }

        internal static void WriteLineProfile(BinaryWriter w, LineProfile v)
        {
            w.Write(v != null); if (v == null) return;
            WriteString(w, v.Id);
            w.Write(v.WarningBlame);
            w.Write(v.Recordings);
            w.Write(v.HasFavourite);
            w.Write(v.FavouriteIncome);
            w.Write(v.BankLedgerDiscount);
            w.Write(v.ListMissedTasks);
            WriteList(w, v.TaskWeights, (b, x) => { b.Write(x); });
        }
        internal static LineProfile ReadLineProfile(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineProfile();
            v.Id = ReadString(r);
            v.WarningBlame = r.ReadInt32();
            v.Recordings = r.ReadInt32();
            v.HasFavourite = ReadBool(r);
            v.FavouriteIncome = r.ReadInt32();
            v.BankLedgerDiscount = r.ReadInt32();
            v.ListMissedTasks = ReadBool(r);
            v.TaskWeights = ReadList(r, b => r.ReadInt32());
            return v;
        }

        internal static void WriteLineConfig(BinaryWriter w, LineConfig v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.ConfigVersion);
            WriteLineDurations(w, v.Durations);
            WriteList(w, v.Tasks, (b, x) => { WriteLineTaskConfig(b, x); });
            WriteLineTaskRules(w, v.TaskRules);
            w.Write(v.RoundsPerDay);
            w.Write(v.ShortWeekDays);
            w.Write(v.FullWeekDays);
            WriteList(w, v.RoundPlayByPlayers, (b, x) => { b.Write(x); });
            w.Write(v.MinSpareTasks);
            WriteEarlySubmitPolicy(w, v.EarlySubmit);
            w.Write(v.CompanyTargetPercent);
            w.Write(v.CareerTargetPercent);
            w.Write(v.CareerNotFired);
            w.Write(v.CareerCompanyTarget);
            w.Write(v.CareerObjective);
            w.Write(v.CareerEmployeeOfMonth);
            w.Write(v.CareerSabotagePenalty);
            w.Write(v.EotmCooperationWeight);
            w.Write(v.StartBlame);
            w.Write(v.LeaderStartBonus);
            w.Write(v.MissedTaskBlame);
            w.Write(v.ExposedSabotageBlame);
            w.Write(v.MaxWarningsPerPlayer);
            w.Write(v.WarningMinVotes);
            w.Write(v.RemedialBlame);
            w.Write(v.RemedialOutput);
            w.Write(v.RemedialIncome);
            w.Write(v.RemedialsPerBlock);
            w.Write(v.RemedialsPerMatch);
            w.Write(v.StartMoney);
            w.Write(v.BlockIncomeFull);
            w.Write(v.BlockIncomeCap);
            w.Write(v.TransferMax);
            w.Write(v.TransferMinInterval);
            w.Write(v.BankLedgerPrice);
            w.Write(v.TaskAuditPrice);
            w.Write(v.ITLogPrice);
            w.Write(v.BadgeLogPrice);
            w.Write(v.PriceStep);
            w.Write(v.PurchasesPerBlock);
            w.Write(v.PurchasesPerMatch);
            w.Write(v.PresentationsPerMeeting);
            w.Write(v.SaverMargin);
            w.Write(v.BrokerReceived);
            w.Write(v.PatronEach);
            w.Write(v.PatronColleagues);
            w.Write(v.TeamPlayerTasks);
            w.Write(v.TeamPlayerOutput);
            w.Write(v.ArchivistRecords);
            w.Write(v.ArchivistColleagues);
            w.Write(v.PresenterRecords);
            w.Write(v.RivalGuardianSameTarget);
            w.Write(v.TricksPerWeekShort);
            w.Write(v.TricksPerWeekFull);
            w.Write(v.UnlockDayFirstWeek);
            w.Write(v.CoffeeSeconds);
            w.Write(v.PhoneDeadSeconds);
            w.Write(v.InternetSeconds);
            w.Write(v.WiretapSeconds);
            w.Write(v.TextMaxChars);
            w.Write(v.EmailMaxChars);
            w.Write(v.ComplaintMaxChars);
            w.Write(v.MeetingChatMaxChars);
            w.Write(v.MessageMinInterval);
            w.Write(v.TextsPerBlock);
            w.Write(v.EmailsPerBlock);
            w.Write(v.ComplaintsPerBlock);
            w.Write(v.MeetingChatPerMeeting);
            w.Write(v.ComplaintPresets);
            w.Write(v.MailTemplates);
            w.Write(v.MessagesKeptPerPlayer);
            w.Write(v.RecordingsEnabled);
            w.Write(v.RecordingMinCallSeconds);
            w.Write(v.RecordingSnippetSeconds);
            w.Write(v.RecordingCandidates);
            WriteRecordingSelection(w, v.RecordingSelection);
            w.Write(v.RecordingFocusWeight);
            w.Write(v.RecordingGroupBonus);
            w.Write(v.RecordingDecisionBonus);
            w.Write(v.RecordingTransferBonus);
            w.Write(v.RecordingExposureDecay);
            w.Write(v.RecordingDecisionWindow);
            w.Write(v.RecordingTransferWindow);
            WriteList(w, v.Profiles, (b, x) => { WriteLineProfile(b, x); });
        }
        internal static LineConfig ReadLineConfig(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineConfig();
            v.ConfigVersion = r.ReadInt32();
            v.Durations = ReadLineDurations(r);
            v.Tasks = ReadList(r, b => ReadLineTaskConfig(b));
            v.TaskRules = ReadLineTaskRules(r);
            v.RoundsPerDay = r.ReadInt32();
            v.ShortWeekDays = r.ReadInt32();
            v.FullWeekDays = r.ReadInt32();
            v.RoundPlayByPlayers = ReadList(r, b => r.ReadSingle());
            v.MinSpareTasks = r.ReadInt32();
            v.EarlySubmit = ReadEarlySubmitPolicy(r);
            v.CompanyTargetPercent = r.ReadInt32();
            v.CareerTargetPercent = r.ReadInt32();
            v.CareerNotFired = r.ReadInt32();
            v.CareerCompanyTarget = r.ReadInt32();
            v.CareerObjective = r.ReadInt32();
            v.CareerEmployeeOfMonth = r.ReadInt32();
            v.CareerSabotagePenalty = r.ReadInt32();
            v.EotmCooperationWeight = r.ReadInt32();
            v.StartBlame = r.ReadInt32();
            v.LeaderStartBonus = r.ReadInt32();
            v.MissedTaskBlame = r.ReadInt32();
            v.ExposedSabotageBlame = r.ReadInt32();
            v.MaxWarningsPerPlayer = r.ReadInt32();
            v.WarningMinVotes = r.ReadInt32();
            v.RemedialBlame = r.ReadInt32();
            v.RemedialOutput = r.ReadInt32();
            v.RemedialIncome = r.ReadInt32();
            v.RemedialsPerBlock = r.ReadInt32();
            v.RemedialsPerMatch = r.ReadInt32();
            v.StartMoney = r.ReadInt32();
            v.BlockIncomeFull = r.ReadInt32();
            v.BlockIncomeCap = r.ReadInt32();
            v.TransferMax = r.ReadInt32();
            v.TransferMinInterval = r.ReadSingle();
            v.BankLedgerPrice = r.ReadInt32();
            v.TaskAuditPrice = r.ReadInt32();
            v.ITLogPrice = r.ReadInt32();
            v.BadgeLogPrice = r.ReadInt32();
            v.PriceStep = r.ReadInt32();
            v.PurchasesPerBlock = r.ReadInt32();
            v.PurchasesPerMatch = r.ReadInt32();
            v.PresentationsPerMeeting = r.ReadInt32();
            v.SaverMargin = r.ReadInt32();
            v.BrokerReceived = r.ReadInt32();
            v.PatronEach = r.ReadInt32();
            v.PatronColleagues = r.ReadInt32();
            v.TeamPlayerTasks = r.ReadInt32();
            v.TeamPlayerOutput = r.ReadInt32();
            v.ArchivistRecords = r.ReadInt32();
            v.ArchivistColleagues = r.ReadInt32();
            v.PresenterRecords = r.ReadInt32();
            v.RivalGuardianSameTarget = r.ReadSingle();
            v.TricksPerWeekShort = r.ReadInt32();
            v.TricksPerWeekFull = r.ReadInt32();
            v.UnlockDayFirstWeek = r.ReadInt32();
            v.CoffeeSeconds = r.ReadSingle();
            v.PhoneDeadSeconds = r.ReadSingle();
            v.InternetSeconds = r.ReadSingle();
            v.WiretapSeconds = r.ReadSingle();
            v.TextMaxChars = r.ReadInt32();
            v.EmailMaxChars = r.ReadInt32();
            v.ComplaintMaxChars = r.ReadInt32();
            v.MeetingChatMaxChars = r.ReadInt32();
            v.MessageMinInterval = r.ReadSingle();
            v.TextsPerBlock = r.ReadInt32();
            v.EmailsPerBlock = r.ReadInt32();
            v.ComplaintsPerBlock = r.ReadInt32();
            v.MeetingChatPerMeeting = r.ReadInt32();
            v.ComplaintPresets = r.ReadInt32();
            v.MailTemplates = r.ReadInt32();
            v.MessagesKeptPerPlayer = r.ReadInt32();
            v.RecordingsEnabled = ReadBool(r);
            v.RecordingMinCallSeconds = r.ReadSingle();
            v.RecordingSnippetSeconds = r.ReadSingle();
            v.RecordingCandidates = r.ReadInt32();
            v.RecordingSelection = ReadRecordingSelection(r);
            v.RecordingFocusWeight = r.ReadSingle();
            v.RecordingGroupBonus = r.ReadSingle();
            v.RecordingDecisionBonus = r.ReadSingle();
            v.RecordingTransferBonus = r.ReadSingle();
            v.RecordingExposureDecay = r.ReadSingle();
            v.RecordingDecisionWindow = r.ReadSingle();
            v.RecordingTransferWindow = r.ReadSingle();
            v.Profiles = ReadList(r, b => ReadLineProfile(b));
            return v;
        }

        internal static void WriteLineView(BinaryWriter w, LineView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.WeekId);
            WriteString(w, v.MatchId);
            WriteString(w, v.RulesetVersion);
            w.Write(v.StateVersion);
            w.Write(v.Me);
            w.Write(v.Now);
            WriteLinePhase(w, v.Phase);
            w.Write(v.PhaseStartTime);
            w.Write(v.PhaseEndTime);
            w.Write(v.Day);
            w.Write(v.DayCount);
            WriteWeekLength(w, v.Length);
            w.Write(v.FirstWeek);
            w.Write(v.RoundIndex);
            w.Write(v.TaskNumber);
            WriteList(w, v.TodayTasks, (b, x) => { WriteTaskKind(b, x); });
            WriteString(w, v.ManagerProfileId);
            w.Write(v.Favourite);
            WriteLineFeaturesView(w, v.Features);
            WriteList(w, v.Players, (b, x) => { WriteLinePlayerView(b, x); });
            WriteLineCompanyView(w, v.Company);
            WriteLineMeView(w, v.Mine);
            WriteLineTaskView(w, v.Task);
            WriteList(w, v.TodayReveals, (b, x) => { WriteLineTaskReveal(b, x); });
            WriteLineMeetingView(w, v.Meeting);
            WriteLineWeekResult(w, v.Result);
        }
        internal static LineView ReadLineView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineView();
            v.WeekId = r.ReadInt32();
            v.MatchId = ReadString(r);
            v.RulesetVersion = ReadString(r);
            v.StateVersion = r.ReadInt64();
            v.Me = r.ReadInt32();
            v.Now = r.ReadDouble();
            v.Phase = ReadLinePhase(r);
            v.PhaseStartTime = r.ReadDouble();
            v.PhaseEndTime = r.ReadDouble();
            v.Day = r.ReadInt32();
            v.DayCount = r.ReadInt32();
            v.Length = ReadWeekLength(r);
            v.FirstWeek = ReadBool(r);
            v.RoundIndex = r.ReadInt32();
            v.TaskNumber = r.ReadInt32();
            v.TodayTasks = ReadList(r, b => ReadTaskKind(b));
            v.ManagerProfileId = ReadString(r);
            v.Favourite = r.ReadInt32();
            v.Features = ReadLineFeaturesView(r);
            v.Players = ReadList(r, b => ReadLinePlayerView(b));
            v.Company = ReadLineCompanyView(r);
            v.Mine = ReadLineMeView(r);
            v.Task = ReadLineTaskView(r);
            v.TodayReveals = ReadList(r, b => ReadLineTaskReveal(b));
            v.Meeting = ReadLineMeetingView(r);
            v.Result = ReadLineWeekResult(r);
            return v;
        }

        internal static void WriteLineFeaturesView(BinaryWriter w, LineFeaturesView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.TricksUnlocked);
            w.Write(v.RecordingsEnabled);
            w.Write(v.IsLastDay);
            w.Write(v.FirstTimeTask);
            w.Write(v.OnboardingDay);
            w.Write(v.CarriedSeconds);
            w.Write(v.CallsOpen);
            w.Write(v.TransfersOpen);
            w.Write(v.EvidenceOpen);
            w.Write(v.PresentationOpen);
            w.Write(v.RemedialOpen);
            w.Write(v.ComplaintsOpen);
            w.Write(v.MeetingChatOpen);
        }
        internal static LineFeaturesView ReadLineFeaturesView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineFeaturesView();
            v.TricksUnlocked = ReadBool(r);
            v.RecordingsEnabled = ReadBool(r);
            v.IsLastDay = ReadBool(r);
            v.FirstTimeTask = ReadBool(r);
            v.OnboardingDay = ReadBool(r);
            v.CarriedSeconds = r.ReadSingle();
            v.CallsOpen = ReadBool(r);
            v.TransfersOpen = ReadBool(r);
            v.EvidenceOpen = ReadBool(r);
            v.PresentationOpen = ReadBool(r);
            v.RemedialOpen = ReadBool(r);
            v.ComplaintsOpen = ReadBool(r);
            v.MeetingChatOpen = ReadBool(r);
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
            w.Write(v.Warnings);
            w.Write(v.Submitted);
            w.Write(v.Voted);
            w.Write(v.SessionPoints);
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
            v.Warnings = r.ReadInt32();
            v.Submitted = ReadBool(r);
            v.Voted = ReadBool(r);
            v.SessionPoints = r.ReadInt32();
            return v;
        }

        internal static void WriteLineCompanyView(BinaryWriter w, LineCompanyView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.ProvisionalPermille);
            w.Write(v.FinalPermille);
            w.Write(v.TargetPercent);
            w.Write(v.ResolvedOutput);
            w.Write(v.ResolvedMax);
            w.Write(v.ScheduledMax);
            w.Write(v.TasksResolved);
            w.Write(v.TasksScheduled);
            WriteCompanyChange(w, v.LastChange);
            WriteTaskKind(w, v.LastTask);
            w.Write(v.LastDelta);
            w.Write(v.Closed);
        }
        internal static LineCompanyView ReadLineCompanyView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineCompanyView();
            v.ProvisionalPermille = r.ReadInt32();
            v.FinalPermille = r.ReadInt32();
            v.TargetPercent = r.ReadInt32();
            v.ResolvedOutput = r.ReadInt32();
            v.ResolvedMax = r.ReadInt32();
            v.ScheduledMax = r.ReadInt32();
            v.TasksResolved = r.ReadInt32();
            v.TasksScheduled = r.ReadInt32();
            v.LastChange = ReadCompanyChange(r);
            v.LastTask = ReadTaskKind(r);
            v.LastDelta = r.ReadInt32();
            v.Closed = ReadBool(r);
            return v;
        }

        internal static void WriteLineMeView(BinaryWriter w, LineMeView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Money);
            w.Write(v.BlockIncome);
            WriteLineObjectiveView(w, v.Objective);
            WriteTrickType(w, v.Trick);
            w.Write(v.TrickUsedToday);
            w.Write(v.TricksLeftThisWeek);
            w.Write(v.ComputerBlockedUntil);
            WriteTrickType(w, v.ComputerBlockedBy);
            w.Write(v.PhoneDeadUntil);
            w.Write(v.WiretapTarget);
            w.Write(v.WiretapUntil);
            WriteLinePhoneView(w, v.Phone);
            WriteList(w, v.CallLog, (b, x) => { WriteLineCallRecordView(b, x); });
            WriteList(w, v.Records, (b, x) => { WriteLineRecordView(b, x); });
            WriteList(w, v.Offers, (b, x) => { WriteLineOfferView(b, x); });
            w.Write(v.PurchasesLeftThisBlock);
            w.Write(v.PurchasesLeftThisMatch);
            w.Write(v.PresentationsLeft);
            w.Write(v.RemedialsLeftThisBlock);
            w.Write(v.RemedialsLeftThisMatch);
            WriteList(w, v.RemedialOptions, (b, x) => { b.Write(x); });
            WriteLineRemedialView(w, v.IncomingRemedial);
            WriteLineRemedialView(w, v.OutgoingRemedial);
            w.Write(v.MessageSeq);
            w.Write(v.TextsLeftThisBlock);
            w.Write(v.EmailsLeftThisBlock);
            w.Write(v.ComplaintsLeftThisBlock);
            w.Write(v.MeetingChatLeft);
            w.Write(v.NextMessageAt);
            w.Write(v.NextTransferAt);
        }
        internal static LineMeView ReadLineMeView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineMeView();
            v.Money = r.ReadInt32();
            v.BlockIncome = r.ReadInt32();
            v.Objective = ReadLineObjectiveView(r);
            v.Trick = ReadTrickType(r);
            v.TrickUsedToday = ReadBool(r);
            v.TricksLeftThisWeek = r.ReadInt32();
            v.ComputerBlockedUntil = r.ReadDouble();
            v.ComputerBlockedBy = ReadTrickType(r);
            v.PhoneDeadUntil = r.ReadDouble();
            v.WiretapTarget = r.ReadInt32();
            v.WiretapUntil = r.ReadDouble();
            v.Phone = ReadLinePhoneView(r);
            v.CallLog = ReadList(r, b => ReadLineCallRecordView(b));
            v.Records = ReadList(r, b => ReadLineRecordView(b));
            v.Offers = ReadList(r, b => ReadLineOfferView(b));
            v.PurchasesLeftThisBlock = r.ReadInt32();
            v.PurchasesLeftThisMatch = r.ReadInt32();
            v.PresentationsLeft = r.ReadInt32();
            v.RemedialsLeftThisBlock = r.ReadInt32();
            v.RemedialsLeftThisMatch = r.ReadInt32();
            v.RemedialOptions = ReadList(r, b => r.ReadInt32());
            v.IncomingRemedial = ReadLineRemedialView(r);
            v.OutgoingRemedial = ReadLineRemedialView(r);
            v.MessageSeq = r.ReadInt64();
            v.TextsLeftThisBlock = r.ReadInt32();
            v.EmailsLeftThisBlock = r.ReadInt32();
            v.ComplaintsLeftThisBlock = r.ReadInt32();
            v.MeetingChatLeft = r.ReadInt32();
            v.NextMessageAt = r.ReadDouble();
            v.NextTransferAt = r.ReadDouble();
            return v;
        }

        internal static void WriteLineObjectiveView(BinaryWriter w, LineObjectiveView v)
        {
            w.Write(v != null); if (v == null) return;
            WriteObjectiveKind(w, v.Kind);
            w.Write(v.Target);
            w.Write(v.Threshold);
            w.Write(v.Progress);
            w.Write(v.Completed);
        }
        internal static LineObjectiveView ReadLineObjectiveView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineObjectiveView();
            v.Kind = ReadObjectiveKind(r);
            v.Target = r.ReadInt32();
            v.Threshold = r.ReadInt32();
            v.Progress = r.ReadInt32();
            v.Completed = ReadBool(r);
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

        internal static void WriteLineTaskView(BinaryWriter w, LineTaskView v)
        {
            w.Write(v != null); if (v == null) return;
            WriteTaskKind(w, v.Kind);
            w.Write(v.Version);
            WriteTaskFamily(w, v.Family);
            w.Write(v.Index);
            w.Write(v.Number);
            w.Write(v.PlayEndTime);
            w.Write(v.Role);
            WriteList(w, v.Group, (b, x) => { b.Write(x); });
            WriteList(w, v.Facts, (b, x) => { WriteLineFact(b, x); });
            WriteList(w, v.PublicFacts, (b, x) => { WriteLineFact(b, x); });
            WriteList(w, v.Controls, (b, x) => { WriteLineControlView(b, x); });
            WriteList(w, v.Items, (b, x) => { b.Write(x); });
            w.Write(v.Submitted);
            w.Write(v.MyTarget);
            w.Write(v.MyA);
            w.Write(v.MyB);
            WriteTaskField(w, v.SubmittedFields);
            WriteLineTaskReveal(w, v.Reveal);
        }
        internal static LineTaskView ReadLineTaskView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineTaskView();
            v.Kind = ReadTaskKind(r);
            v.Version = r.ReadInt32();
            v.Family = ReadTaskFamily(r);
            v.Index = r.ReadInt32();
            v.Number = r.ReadInt32();
            v.PlayEndTime = r.ReadDouble();
            v.Role = r.ReadInt32();
            v.Group = ReadList(r, b => r.ReadInt32());
            v.Facts = ReadList(r, b => ReadLineFact(b));
            v.PublicFacts = ReadList(r, b => ReadLineFact(b));
            v.Controls = ReadList(r, b => ReadLineControlView(b));
            v.Items = ReadList(r, b => r.ReadInt32());
            v.Submitted = ReadBool(r);
            v.MyTarget = r.ReadInt32();
            v.MyA = r.ReadInt32();
            v.MyB = r.ReadInt32();
            v.SubmittedFields = ReadTaskField(r);
            v.Reveal = ReadLineTaskReveal(r);
            return v;
        }

        internal static void WriteLineFact(BinaryWriter w, LineFact v)
        {
            w.Write(v != null); if (v == null) return;
            WriteString(w, v.Key);
            w.Write(v.Scope);
            w.Write(v.P);
            w.Write(v.N1);
            w.Write(v.N2);
            w.Write(v.N3);
        }
        internal static LineFact ReadLineFact(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineFact();
            v.Key = ReadString(r);
            v.Scope = r.ReadInt32();
            v.P = r.ReadInt32();
            v.N1 = r.ReadInt32();
            v.N2 = r.ReadInt32();
            v.N3 = r.ReadInt32();
            return v;
        }

        internal static void WriteLineControlView(BinaryWriter w, LineControlView v)
        {
            w.Write(v != null); if (v == null) return;
            WriteControlKind(w, v.Kind);
            WriteTaskField(w, v.Field);
            WriteString(w, v.Key);
            WriteList(w, v.Options, (b, x) => { WriteLineOption(b, x); });
            w.Write(v.Min);
            w.Write(v.Max);
            w.Write(v.Digits);
            w.Write(v.MaxPicks);
            w.Write(v.AllowSelf);
            w.Write(v.AllowNone);
            w.Write(v.Required);
            w.Write(v.Locks);
            w.Write(v.Sabotage);
            w.Write(v.WaitingFor);
            w.Write(v.Enabled);
        }
        internal static LineControlView ReadLineControlView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineControlView();
            v.Kind = ReadControlKind(r);
            v.Field = ReadTaskField(r);
            v.Key = ReadString(r);
            v.Options = ReadList(r, b => ReadLineOption(b));
            v.Min = r.ReadInt32();
            v.Max = r.ReadInt32();
            v.Digits = r.ReadInt32();
            v.MaxPicks = r.ReadInt32();
            v.AllowSelf = ReadBool(r);
            v.AllowNone = ReadBool(r);
            v.Required = ReadBool(r);
            v.Locks = ReadBool(r);
            v.Sabotage = ReadBool(r);
            v.WaitingFor = r.ReadInt32();
            v.Enabled = ReadBool(r);
            return v;
        }

        internal static void WriteLineOption(BinaryWriter w, LineOption v)
        {
            w.Write(v != null); if (v == null) return;
            WriteString(w, v.Key);
            w.Write(v.Value);
            w.Write(v.P);
            w.Write(v.N1);
            w.Write(v.N2);
            w.Write(v.Sabotage);
            w.Write(v.Enabled);
        }
        internal static LineOption ReadLineOption(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineOption();
            v.Key = ReadString(r);
            v.Value = r.ReadInt32();
            v.P = r.ReadInt32();
            v.N1 = r.ReadInt32();
            v.N2 = r.ReadInt32();
            v.Sabotage = ReadBool(r);
            v.Enabled = ReadBool(r);
            return v;
        }

        internal static void WriteLineTaskReveal(BinaryWriter w, LineTaskReveal v)
        {
            w.Write(v != null); if (v == null) return;
            WriteTaskKind(w, v.Kind);
            w.Write(v.Index);
            w.Write(v.Number);
            w.Write(v.Output);
            w.Write(v.MaxOutput);
            WriteList(w, v.PublicFacts, (b, x) => { WriteLineFact(b, x); });
            WriteList(w, v.Votes, (b, x) => { WriteLineVoteView(b, x); });
            WriteList(w, v.Missed, (b, x) => { b.Write(x); });
            WriteList(w, v.Deltas, (b, x) => { WriteLineBlameDelta(b, x); });
        }
        internal static LineTaskReveal ReadLineTaskReveal(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineTaskReveal();
            v.Kind = ReadTaskKind(r);
            v.Index = r.ReadInt32();
            v.Number = r.ReadInt32();
            v.Output = r.ReadInt32();
            v.MaxOutput = r.ReadInt32();
            v.PublicFacts = ReadList(r, b => ReadLineFact(b));
            v.Votes = ReadList(r, b => ReadLineVoteView(b));
            v.Missed = ReadList(r, b => r.ReadInt32());
            v.Deltas = ReadList(r, b => ReadLineBlameDelta(b));
            return v;
        }

        internal static void WriteLineVoteView(BinaryWriter w, LineVoteView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Voter);
            w.Write(v.Target);
        }
        internal static LineVoteView ReadLineVoteView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineVoteView();
            v.Voter = r.ReadInt32();
            v.Target = r.ReadInt32();
            return v;
        }

        internal static void WriteLineBlameDelta(BinaryWriter w, LineBlameDelta v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Player);
            w.Write(v.Delta);
            WriteBlameReason(w, v.Reason);
            WriteTaskKind(w, v.Task);
            w.Write(v.Day);
        }
        internal static LineBlameDelta ReadLineBlameDelta(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineBlameDelta();
            v.Player = r.ReadInt32();
            v.Delta = r.ReadInt32();
            v.Reason = ReadBlameReason(r);
            v.Task = ReadTaskKind(r);
            v.Day = r.ReadInt32();
            return v;
        }

        internal static void WriteLineRecordView(BinaryWriter w, LineRecordView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Id);
            WriteRecordKind(w, v.Kind);
            WriteRecordSource(w, v.Source);
            w.Write(v.Time);
            w.Write(v.Day);
            w.Write(v.TaskNumber);
            WriteTaskKind(w, v.Task);
            w.Write(v.Actor);
            w.Write(v.Subject);
            w.Write(v.N1);
            w.Write(v.N2);
            w.Write(v.N3);
            w.Write(v.ProofOf);
            w.Write(v.Presented);
        }
        internal static LineRecordView ReadLineRecordView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineRecordView();
            v.Id = r.ReadInt32();
            v.Kind = ReadRecordKind(r);
            v.Source = ReadRecordSource(r);
            v.Time = r.ReadDouble();
            v.Day = r.ReadInt32();
            v.TaskNumber = r.ReadInt32();
            v.Task = ReadTaskKind(r);
            v.Actor = r.ReadInt32();
            v.Subject = r.ReadInt32();
            v.N1 = r.ReadInt32();
            v.N2 = r.ReadInt32();
            v.N3 = r.ReadInt32();
            v.ProofOf = r.ReadInt32();
            v.Presented = ReadBool(r);
            return v;
        }

        internal static void WriteLineOfferView(BinaryWriter w, LineOfferView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Id);
            WriteOfferKind(w, v.Kind);
            w.Write(v.Day);
            w.Write(v.TaskNumber);
            WriteTaskKind(w, v.Task);
            w.Write(v.Price);
            w.Write(v.Purchased);
            w.Write(v.Available);
        }
        internal static LineOfferView ReadLineOfferView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineOfferView();
            v.Id = r.ReadInt32();
            v.Kind = ReadOfferKind(r);
            v.Day = r.ReadInt32();
            v.TaskNumber = r.ReadInt32();
            v.Task = ReadTaskKind(r);
            v.Price = r.ReadInt32();
            v.Purchased = ReadBool(r);
            v.Available = ReadBool(r);
            return v;
        }

        internal static void WriteLineRemedialView(BinaryWriter w, LineRemedialView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Requester);
            w.Write(v.Cosigner);
            w.Write(v.TaskNumber);
            WriteTaskKind(w, v.Task);
            w.Write(v.ExpiresAt);
        }
        internal static LineRemedialView ReadLineRemedialView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineRemedialView();
            v.Requester = r.ReadInt32();
            v.Cosigner = r.ReadInt32();
            v.TaskNumber = r.ReadInt32();
            v.Task = ReadTaskKind(r);
            v.ExpiresAt = r.ReadDouble();
            return v;
        }

        internal static void WriteLineMessage(BinaryWriter w, LineMessage v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Seq);
            w.Write(v.Id);
            WriteMessageKind(w, v.Kind);
            w.Write(v.ThreadId);
            w.Write(v.ReplyTo);
            w.Write(v.From);
            w.Write(v.To);
            w.Write(v.ToAll);
            WriteString(w, v.Text);
            w.Write(v.TemplateId);
            w.Write(v.About);
            w.Write(v.Time);
            w.Write(v.Day);
        }
        internal static LineMessage ReadLineMessage(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineMessage();
            v.Seq = r.ReadInt64();
            v.Id = r.ReadInt32();
            v.Kind = ReadMessageKind(r);
            v.ThreadId = r.ReadInt32();
            v.ReplyTo = r.ReadInt32();
            v.From = r.ReadInt32();
            v.To = r.ReadInt32();
            v.ToAll = ReadBool(r);
            v.Text = ReadString(r);
            v.TemplateId = r.ReadInt32();
            v.About = r.ReadInt32();
            v.Time = r.ReadDouble();
            v.Day = r.ReadInt32();
            return v;
        }

        internal static void WriteLineMeetingView(BinaryWriter w, LineMeetingView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.IsFinal);
            WriteList(w, v.Dossier, (b, x) => { WriteLineDossierEntry(b, x); });
            WriteList(w, v.Recordings, (b, x) => { WriteLineRecordingPick(b, x); });
            WriteList(w, v.Complaints, (b, x) => { WriteLineComplaintView(b, x); });
            WriteList(w, v.Presentations, (b, x) => { WriteLinePresentationView(b, x); });
            w.Write(v.MyVote);
            w.Write(v.WarningTo);
            WriteVoteOutcome(w, v.Outcome);
            WriteList(w, v.Votes, (b, x) => { WriteLineVoteView(b, x); });
            WriteList(w, v.Roster, (b, x) => { b.Write(x); });
            w.Write(v.MajorityNeeded);
            WriteList(w, v.DefenseOrder, (b, x) => { b.Write(x); });
            w.Write(v.Defender);
            w.Write(v.DefenderEndTime);
            w.Write(v.Fired);
            w.Write(v.CompanyClosed);
        }
        internal static LineMeetingView ReadLineMeetingView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineMeetingView();
            v.IsFinal = ReadBool(r);
            v.Dossier = ReadList(r, b => ReadLineDossierEntry(b));
            v.Recordings = ReadList(r, b => ReadLineRecordingPick(b));
            v.Complaints = ReadList(r, b => ReadLineComplaintView(b));
            v.Presentations = ReadList(r, b => ReadLinePresentationView(b));
            v.MyVote = r.ReadInt32();
            v.WarningTo = r.ReadInt32();
            v.Outcome = ReadVoteOutcome(r);
            v.Votes = ReadList(r, b => ReadLineVoteView(b));
            v.Roster = ReadList(r, b => r.ReadInt32());
            v.MajorityNeeded = r.ReadInt32();
            v.DefenseOrder = ReadList(r, b => r.ReadInt32());
            v.Defender = r.ReadInt32();
            v.DefenderEndTime = r.ReadDouble();
            v.Fired = r.ReadInt32();
            v.CompanyClosed = ReadBool(r);
            return v;
        }

        internal static void WriteLineDossierEntry(BinaryWriter w, LineDossierEntry v)
        {
            w.Write(v != null); if (v == null) return;
            WriteDossierKind(w, v.Kind);
            WriteTaskKind(w, v.Task);
            w.Write(v.TaskNumber);
            WriteTrickType(w, v.Trick);
            w.Write(v.Player);
            w.Write(v.Victim);
            w.Write(v.Arg);
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
            v.TaskNumber = r.ReadInt32();
            v.Trick = ReadTrickType(r);
            v.Player = r.ReadInt32();
            v.Victim = r.ReadInt32();
            v.Arg = r.ReadInt32();
            v.N1 = r.ReadInt32();
            v.N2 = r.ReadInt32();
            v.Time = r.ReadDouble();
            return v;
        }

        internal static void WriteLineComplaintView(BinaryWriter w, LineComplaintView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Target);
            w.Write(v.Preset);
            WriteString(w, v.Text);
        }
        internal static LineComplaintView ReadLineComplaintView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineComplaintView();
            v.Target = r.ReadInt32();
            v.Preset = r.ReadInt32();
            v.Text = ReadString(r);
            return v;
        }

        internal static void WriteLinePresentationView(BinaryWriter w, LinePresentationView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Presenter);
            WriteLineRecordView(w, v.Record);
            w.Write(v.Time);
            w.Write(v.ExposedIncident);
            WriteIncidentKind(w, v.ExposedKind);
            w.Write(v.Repeat);
        }
        internal static LinePresentationView ReadLinePresentationView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LinePresentationView();
            v.Presenter = r.ReadInt32();
            v.Record = ReadLineRecordView(r);
            v.Time = r.ReadDouble();
            v.ExposedIncident = r.ReadInt32();
            v.ExposedKind = ReadIncidentKind(r);
            v.Repeat = ReadBool(r);
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
            WriteTaskKind(w, v.Context);
            w.Write(v.Played);
        }
        internal static LineRecordingPick ReadLineRecordingPick(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineRecordingPick();
            v.Index = r.ReadInt32();
            v.CandidateCallIds = ReadList(r, b => r.ReadInt32());
            v.PlayAt = r.ReadDouble();
            v.Seconds = r.ReadSingle();
            v.ChosenCallId = r.ReadInt32();
            v.Caller = r.ReadInt32();
            v.Callee = r.ReadInt32();
            v.CallStart = r.ReadDouble();
            v.Context = ReadTaskKind(r);
            v.Played = ReadBool(r);
            return v;
        }

        internal static void WriteLineIncidentView(BinaryWriter w, LineIncidentView v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Id);
            WriteIncidentKind(w, v.Kind);
            w.Write(v.Actor);
            w.Write(v.Day);
            w.Write(v.TaskNumber);
            w.Write(v.ExposedBy);
            w.Write(v.Time);
        }
        internal static LineIncidentView ReadLineIncidentView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineIncidentView();
            v.Id = r.ReadInt32();
            v.Kind = ReadIncidentKind(r);
            v.Actor = r.ReadInt32();
            v.Day = r.ReadInt32();
            v.TaskNumber = r.ReadInt32();
            v.ExposedBy = r.ReadInt32();
            v.Time = r.ReadDouble();
            return v;
        }

        internal static void WriteLineCareerLine(BinaryWriter w, LineCareerLine v)
        {
            w.Write(v != null); if (v == null) return;
            WriteCareerItem(w, v.Item);
            w.Write(v.Points);
            w.Write(v.Count);
        }
        internal static LineCareerLine ReadLineCareerLine(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineCareerLine();
            v.Item = ReadCareerItem(r);
            v.Points = r.ReadInt32();
            v.Count = r.ReadInt32();
            return v;
        }

        internal static void WriteLineWeekResult(BinaryWriter w, LineWeekResult v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.WeekId);
            WriteString(w, v.MatchId);
            WriteString(w, v.RulesetVersion);
            WriteString(w, v.ManagerProfileId);
            w.Write(v.Days);
            w.Write(v.Fired);
            WriteVoteOutcome(w, v.FinalOutcome);
            w.Write(v.CompanyClosed);
            w.Write(v.FinalProductivityPermille);
            w.Write(v.TargetPercent);
            WriteString(w, v.NextManagerPick);
            w.Write(v.IncidentsTotal);
            WriteList(w, v.Exposed, (b, x) => { WriteLineIncidentView(b, x); });
            WriteList(w, v.Players, (b, x) => { WriteLineWeekPlayerResult(b, x); });
            w.Write(v.ResultHash);
        }
        internal static LineWeekResult ReadLineWeekResult(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineWeekResult();
            v.WeekId = r.ReadInt32();
            v.MatchId = ReadString(r);
            v.RulesetVersion = ReadString(r);
            v.ManagerProfileId = ReadString(r);
            v.Days = r.ReadInt32();
            v.Fired = r.ReadInt32();
            v.FinalOutcome = ReadVoteOutcome(r);
            v.CompanyClosed = ReadBool(r);
            v.FinalProductivityPermille = r.ReadInt32();
            v.TargetPercent = r.ReadInt32();
            v.NextManagerPick = ReadString(r);
            v.IncidentsTotal = r.ReadInt32();
            v.Exposed = ReadList(r, b => ReadLineIncidentView(b));
            v.Players = ReadList(r, b => ReadLineWeekPlayerResult(b));
            v.ResultHash = r.ReadUInt64();
            return v;
        }

        internal static void WriteLineWeekPlayerResult(BinaryWriter w, LineWeekPlayerResult v)
        {
            w.Write(v != null); if (v == null) return;
            w.Write(v.Player);
            w.Write(v.Blame);
            w.Write(v.Warnings);
            w.Write(v.Fired);
            WriteObjectiveKind(w, v.Objective);
            w.Write(v.ObjectiveTarget);
            w.Write(v.ObjectiveDone);
            w.Write(v.EmployeeOfMonth);
            WriteList(w, v.Career, (b, x) => { WriteLineCareerLine(b, x); });
            w.Write(v.CareerTotal);
            w.Write(v.Rank);
            w.Write(v.Money);
            w.Write(v.ContributionQuality);
            w.Write(v.TasksConnected);
            w.Write(v.ExposedIncidents);
            w.Write(v.SessionPoints);
        }
        internal static LineWeekPlayerResult ReadLineWeekPlayerResult(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineWeekPlayerResult();
            v.Player = r.ReadInt32();
            v.Blame = r.ReadInt32();
            v.Warnings = r.ReadInt32();
            v.Fired = ReadBool(r);
            v.Objective = ReadObjectiveKind(r);
            v.ObjectiveTarget = r.ReadInt32();
            v.ObjectiveDone = ReadBool(r);
            v.EmployeeOfMonth = ReadBool(r);
            v.Career = ReadList(r, b => ReadLineCareerLine(b));
            v.CareerTotal = r.ReadInt32();
            v.Rank = r.ReadInt32();
            v.Money = r.ReadInt32();
            v.ContributionQuality = r.ReadInt32();
            v.TasksConnected = r.ReadInt32();
            v.ExposedIncidents = r.ReadInt32();
            v.SessionPoints = r.ReadInt32();
            return v;
        }

    }
}
