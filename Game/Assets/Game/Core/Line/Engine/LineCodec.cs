using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// Explicit v3 little-endian wire schema. The header is magic, UInt16 protocol, message kind.
    /// Enums and booleans occupy one byte. Strings/lists use unsigned LEB128 count + 1 (zero = null).
    /// Reference objects use a strict presence byte. No runtime reflection or v2 dependencies.
    /// </summary>
    public static class LineCodec
    {
        private const byte Magic = 0xB3;
        private const int MaxPayload = 1024 * 1024;
        private const int MaxItems = 16384;
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

        internal static LineView Clone(LineView value) { return DecodeView(EncodeView(value)); }
        internal static LineSetup Clone(LineSetup value) { return Decode(8, value, WriteLineSetup, ReadLineSetup); }
        internal static LineConfig Clone(LineConfig value) { return Decode(9, value, WriteLineConfig, ReadLineConfig); }
        internal static LineSessionHistory Clone(LineSessionHistory value) { return Decode(10, value, WriteLineSessionHistory, ReadLineSessionHistory); }
        internal static LineWeekResult Clone(LineWeekResult value) { return Decode(11, value, WriteLineWeekResult, ReadLineWeekResult); }

        internal static ulong HashResult(LineWeekResult value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var copy = Clone(value);
            copy.ResultHash = 0;
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
                if (stream.Length > MaxPayload) throw new FormatException("v3 payload too large.");
                return stream.ToArray();
            }
        }

        private static T Decode<T>(byte[] payload, byte kind, Func<BinaryReader, T> read)
        {
            if (payload == null || payload.Length > MaxPayload) throw new FormatException("Invalid v3 payload.");
            try
            {
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadByte() != Magic || reader.ReadUInt16() != LineProtocol.Version || reader.ReadByte() != kind)
                        throw new FormatException("Incompatible v3 message.");
                    T value = read(reader);
                    if (stream.Position != stream.Length) throw new FormatException("Trailing v3 payload bytes.");
                    return value;
                }
            }
            catch (IOException ex) { throw new FormatException("Truncated v3 payload.", ex); }
            catch (ArgumentException ex) { throw new FormatException("Malformed v3 payload.", ex); }
            catch (OverflowException ex) { throw new FormatException("Invalid v3 size.", ex); }
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
            if (bytes.Length > MaxPayload) throw new FormatException("String too long.");
            WriteLength(w, bytes.Length); w.Write(bytes);
        }

        private static string ReadString(BinaryReader r)
        {
            int length = ReadLength(r, MaxPayload);
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

        // Field order below is the contract declaration order, written explicitly for AOT/IL2CPP.

        internal static void WriteLineAction(BinaryWriter w, LineAction v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.WeekId);
            w.Write(v.RequestId);
            w.Write((byte)v.Verb);
            w.Write(v.Target);
            w.Write(v.A);
            w.Write(v.B);
        }

        internal static LineAction ReadLineAction(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineAction();
            v.WeekId = r.ReadInt32();
            v.RequestId = r.ReadUInt32();
            v.Verb = (LineVerb)ReadEnum(r, 12);
            v.Target = r.ReadInt32();
            v.A = r.ReadInt32();
            v.B = r.ReadInt32();
            return v;
        }

        internal static void WriteLineActionResult(BinaryWriter w, LineActionResult v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.RequestId);
            w.Write(v.Accepted);
            w.Write((byte)v.Failure);
        }

        internal static LineActionResult ReadLineActionResult(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineActionResult();
            v.RequestId = r.ReadUInt32();
            v.Accepted = ReadBool(r);
            v.Failure = (LineFailure)ReadEnum(r, 20);
            return v;
        }

        internal static void WriteLineEvent(BinaryWriter w, LineEvent v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write((byte)v.Type);
            w.Write(v.Time);
            w.Write((byte)v.Audience);
            w.Write(v.AudiencePlayer);
            w.Write(v.Actor);
            w.Write(v.Target);
            w.Write(v.A);
            w.Write(v.B);
        }

        internal static LineEvent ReadLineEvent(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineEvent();
            v.Type = (LineEventType)ReadEnum(r, 23);
            v.Time = r.ReadDouble();
            v.Audience = (LineAudience)ReadEnum(r, 2);
            v.AudiencePlayer = r.ReadInt32();
            v.Actor = r.ReadInt32();
            v.Target = r.ReadInt32();
            v.A = r.ReadInt32();
            v.B = r.ReadInt32();
            return v;
        }

        internal static void WriteLinePlayerInfo(BinaryWriter w, LinePlayerInfo v)
        {
            w.Write(v != null);
            if (v == null) return;
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
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.WeekId);
            w.Write(v.Seed);
            w.Write(v.StartTime);
            w.Write((byte)v.Length);
            WriteList(w, v.Players, (writer, item) => { WriteLinePlayerInfo(writer, item); });
            WriteString(w, v.ManagerProfileId);
            w.Write(v.RecordingsEnabled);
            WriteLineSessionHistory(w, v.History);
            WriteList(w, v.ForceAgendas, (writer, item) => { writer.Write(item); });
            WriteList(w, v.ForceTricks, (writer, item) => { writer.Write((byte)item); });
            WriteList(w, v.ForceRounds, (writer, item) => { writer.Write((byte)item); });
            WriteList(w, v.ForceHidden, (writer, item) => { writer.Write(item); });
        }

        internal static LineSetup ReadLineSetup(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineSetup();
            v.WeekId = r.ReadInt32();
            v.Seed = r.ReadInt32();
            v.StartTime = r.ReadDouble();
            v.Length = (WeekLength)ReadEnum(r, 1);
            v.Players = ReadList(r, reader => ReadLinePlayerInfo(reader));
            v.ManagerProfileId = ReadString(r);
            v.RecordingsEnabled = ReadBool(r);
            v.History = ReadLineSessionHistory(r);
            v.ForceAgendas = ReadList(r, reader => reader.ReadInt32());
            v.ForceTricks = ReadList(r, reader => (TrickType)ReadEnum(reader, 6));
            v.ForceRounds = ReadList(r, reader => (RoundType)ReadEnum(reader, 6));
            v.ForceHidden = ReadList(r, reader => reader.ReadInt32());
            return v;
        }

        internal static void WriteLineSessionHistory(BinaryWriter w, LineSessionHistory v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.WeeksPlayed);
            WriteList(w, v.SessionPoints, (writer, item) => { writer.Write(item); });
            WriteList(w, v.PastManagers, (writer, item) => { WriteString(writer, item); });
            WriteList(w, v.SeenRounds, (writer, item) => { writer.Write((byte)item); });
            WriteList(w, v.UsedTopics, (writer, item) => { writer.Write(item); });
            WriteList(w, v.TimesFired, (writer, item) => { writer.Write(item); });
            WriteString(w, v.NextManager);
            WriteList(w, v.HiddenRoles, (writer, item) => { writer.Write(item); });
            WriteList(w, v.RoundUses, (writer, item) => { writer.Write(item); });
        }

        internal static LineSessionHistory ReadLineSessionHistory(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineSessionHistory();
            v.WeeksPlayed = r.ReadInt32();
            v.SessionPoints = ReadList(r, reader => reader.ReadInt32());
            v.PastManagers = ReadList(r, reader => ReadString(reader));
            v.SeenRounds = ReadList(r, reader => (RoundType)ReadEnum(reader, 6));
            v.UsedTopics = ReadList(r, reader => reader.ReadInt32());
            v.TimesFired = ReadList(r, reader => reader.ReadInt32());
            v.NextManager = ReadString(r);
            v.HiddenRoles = ReadList(r, reader => reader.ReadInt32());
            v.RoundUses = ReadList(r, reader => reader.ReadInt32());
            return v;
        }

        internal static void WriteLineLobbyView(BinaryWriter w, LineLobbyView v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.ProtocolVersion);
            WriteString(w, v.BuildId);
            WriteList(w, v.Slots, (writer, item) => { WriteLineLobbySlot(writer, item); });
            w.Write(v.WeekRunning);
            w.Write(v.WeekId);
            w.Write((byte)v.Length);
            w.Write(v.RecordingsEnabled);
            w.Write(v.WeeksPlayed);
            WriteString(w, v.StatusKey);
        }

        internal static LineLobbyView ReadLineLobbyView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineLobbyView();
            v.ProtocolVersion = r.ReadInt32();
            v.BuildId = ReadString(r);
            v.Slots = ReadList(r, reader => ReadLineLobbySlot(reader));
            v.WeekRunning = ReadBool(r);
            v.WeekId = r.ReadInt32();
            v.Length = (WeekLength)ReadEnum(r, 1);
            v.RecordingsEnabled = ReadBool(r);
            v.WeeksPlayed = r.ReadInt32();
            v.StatusKey = ReadString(r);
            return v;
        }

        internal static void WriteLineLobbySlot(BinaryWriter w, LineLobbySlot v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.Slot);
            w.Write(v.Occupied);
            w.Write(v.Connected);
            w.Write(v.Ready);
            w.Write(v.IsHost);
            w.Write(v.IsBot);
            WriteString(w, v.Name);
            w.Write(v.SessionPoints);
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
            return v;
        }

        internal static void WriteLineConnectionPayload(BinaryWriter w, LineConnectionPayload v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.ProtocolVersion);
            WriteString(w, v.BuildId);
            WriteString(w, v.PlayerName);
            WriteString(w, v.ReconnectToken);
            WriteString(w, v.Language);
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
            return v;
        }

        internal static void WriteLineWelcome(BinaryWriter w, LineWelcome v)
        {
            w.Write(v != null);
            if (v == null) return;
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
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.Morning);
            w.Write(v.MorningFirst);
            w.Write(v.RoundIntro);
            w.Write(v.RoundIntroFirst);
            w.Write(v.RoundPlay);
            w.Write(v.AllSubmittedRemaining);
            w.Write(v.RoundReveal);
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
            return v;
        }

        internal static void WriteLineRoundRules(BinaryWriter w, LineRoundRules v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.PrimBothShare);
            w.Write(v.PrimTakerWins);
            w.Write(v.PrimSharerLoses);
            w.Write(v.PrimBothTake);
            w.Write(v.InternShareChance);
            w.Write(v.PrimTrioNobodyTakes);
            w.Write(v.PrimTrioSoloTaker);
            w.Write(v.PrimTrioPairTakers);
            w.Write(v.PrimTrioSharer);
            w.Write(v.PrimTrioAllTake);
            w.Write(v.HiddenCaught);
            w.Write(v.OddCaughtButGuessed);
            w.Write(v.CorrectVoter);
            w.Write(v.HiddenEscaped);
            w.Write(v.WrongInnocent);
            w.Write(v.OddGuessOptions);
            w.Write(v.KayipRaporMinPlayers);
            w.Write(v.ComplaintTop);
            w.Write(v.ComplaintNone);
            w.Write(v.ComplaintReasons);
            w.Write(v.ComplaintMinPlayers);
            w.Write(v.PromotionWinner);
            w.Write(v.PromotionBacker);
            w.Write(v.PasswordCracked);
            w.Write(v.PasswordCrackedSaboteur);
            w.Write(v.PasswordFailed);
            w.Write(v.PasswordFailedSaboteur);
            w.Write(v.AttemptsPerPlayer);
        }

        internal static LineRoundRules ReadLineRoundRules(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineRoundRules();
            v.PrimBothShare = r.ReadInt32();
            v.PrimTakerWins = r.ReadInt32();
            v.PrimSharerLoses = r.ReadInt32();
            v.PrimBothTake = r.ReadInt32();
            v.InternShareChance = r.ReadSingle();
            v.PrimTrioNobodyTakes = r.ReadInt32();
            v.PrimTrioSoloTaker = r.ReadInt32();
            v.PrimTrioPairTakers = r.ReadInt32();
            v.PrimTrioSharer = r.ReadInt32();
            v.PrimTrioAllTake = r.ReadInt32();
            v.HiddenCaught = r.ReadInt32();
            v.OddCaughtButGuessed = r.ReadInt32();
            v.CorrectVoter = r.ReadInt32();
            v.HiddenEscaped = r.ReadInt32();
            v.WrongInnocent = r.ReadInt32();
            v.OddGuessOptions = r.ReadInt32();
            v.KayipRaporMinPlayers = r.ReadInt32();
            v.ComplaintTop = r.ReadInt32();
            v.ComplaintNone = r.ReadInt32();
            v.ComplaintReasons = r.ReadInt32();
            v.ComplaintMinPlayers = r.ReadInt32();
            v.PromotionWinner = r.ReadInt32();
            v.PromotionBacker = r.ReadInt32();
            v.PasswordCracked = r.ReadInt32();
            v.PasswordCrackedSaboteur = r.ReadInt32();
            v.PasswordFailed = r.ReadInt32();
            v.PasswordFailedSaboteur = r.ReadInt32();
            v.AttemptsPerPlayer = r.ReadInt32();
            return v;
        }

        internal static void WriteLineProfile(BinaryWriter w, LineProfile v)
        {
            w.Write(v != null);
            if (v == null) return;
            WriteString(w, v.Id);
            w.Write(v.IhtarBlame);
            w.Write(v.FinalVoteWeight);
            w.Write(v.GreedBonus);
            w.Write(v.TraceChance);
            w.Write(v.TrickCaughtBlame);
            w.Write(v.LeakChance);
            w.Write(v.Recordings);
            w.Write(v.HasFavourite);
        }

        internal static LineProfile ReadLineProfile(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineProfile();
            v.Id = ReadString(r);
            v.IhtarBlame = r.ReadInt32();
            v.FinalVoteWeight = r.ReadInt32();
            v.GreedBonus = r.ReadInt32();
            v.TraceChance = r.ReadSingle();
            v.TrickCaughtBlame = r.ReadInt32();
            v.LeakChance = r.ReadSingle();
            v.Recordings = r.ReadInt32();
            v.HasFavourite = ReadBool(r);
            return v;
        }

        internal static void WriteLineConfig(BinaryWriter w, LineConfig v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.ConfigVersion);
            WriteLineDurations(w, v.Durations);
            WriteLineRoundRules(w, v.Rounds);
            w.Write(v.RoundsPerDay);
            w.Write(v.ShortWeekDays);
            w.Write(v.FullWeekDays);
            w.Write(v.StartBlame);
            w.Write(v.LeaderStartBonus);
            w.Write(v.MaxIhtarsPerPlayerPerWeek);
            w.Write(v.FinalShortlistSmall);
            w.Write(v.FinalShortlistLarge);
            WriteList(w, v.RoundPlayByPlayers, (writer, item) => { writer.Write(item); });
            w.Write(v.TricksPerWeekShort);
            w.Write(v.TricksPerWeekFull);
            w.Write(v.TipsPerWeek);
            w.Write(v.TipUnlockDayFirstWeek);
            w.Write(v.TopicCategories);
            w.Write(v.TopicsPerCategory);
            w.Write(v.AgendaPointsPerIhtar);
            w.Write(v.RecordingFocusWeight);
            w.Write(v.SurvivePoints);
            w.Write(v.AgendaPoints);
            w.Write(v.EmployeeOfWeekPoints);
            w.Write(v.UnlockDayFirstWeek);
            w.Write(v.CoffeeSeconds);
            w.Write(v.PhoneDeadSeconds);
            w.Write(v.InternetSeconds);
            w.Write(v.WiretapSeconds);
            w.Write(v.FalseTipBlame);
            w.Write(v.ConfirmedTipBlame);
            w.Write(v.MailTemplates);
            w.Write(v.MailsPerDay);
            w.Write(v.TopicPairs);
            w.Write(v.RecordingsEnabled);
            w.Write(v.RecordingMinCallSeconds);
            w.Write(v.RecordingSnippetSeconds);
            w.Write(v.RecordingCandidates);
            WriteList(w, v.Profiles, (writer, item) => { WriteLineProfile(writer, item); });
        }

        internal static LineConfig ReadLineConfig(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineConfig();
            v.ConfigVersion = r.ReadInt32();
            v.Durations = ReadLineDurations(r);
            v.Rounds = ReadLineRoundRules(r);
            v.RoundsPerDay = r.ReadInt32();
            v.ShortWeekDays = r.ReadInt32();
            v.FullWeekDays = r.ReadInt32();
            v.StartBlame = r.ReadInt32();
            v.LeaderStartBonus = r.ReadInt32();
            v.MaxIhtarsPerPlayerPerWeek = r.ReadInt32();
            v.FinalShortlistSmall = r.ReadInt32();
            v.FinalShortlistLarge = r.ReadInt32();
            v.RoundPlayByPlayers = ReadList(r, reader => reader.ReadSingle());
            v.TricksPerWeekShort = r.ReadInt32();
            v.TricksPerWeekFull = r.ReadInt32();
            v.TipsPerWeek = r.ReadInt32();
            v.TipUnlockDayFirstWeek = r.ReadInt32();
            v.TopicCategories = r.ReadInt32();
            v.TopicsPerCategory = r.ReadInt32();
            v.AgendaPointsPerIhtar = r.ReadInt32();
            v.RecordingFocusWeight = r.ReadSingle();
            v.SurvivePoints = r.ReadInt32();
            v.AgendaPoints = r.ReadInt32();
            v.EmployeeOfWeekPoints = r.ReadInt32();
            v.UnlockDayFirstWeek = r.ReadInt32();
            v.CoffeeSeconds = r.ReadSingle();
            v.PhoneDeadSeconds = r.ReadSingle();
            v.InternetSeconds = r.ReadSingle();
            v.WiretapSeconds = r.ReadSingle();
            v.FalseTipBlame = r.ReadInt32();
            v.ConfirmedTipBlame = r.ReadInt32();
            v.MailTemplates = r.ReadInt32();
            v.MailsPerDay = r.ReadInt32();
            v.TopicPairs = r.ReadInt32();
            v.RecordingsEnabled = ReadBool(r);
            v.RecordingMinCallSeconds = r.ReadSingle();
            v.RecordingSnippetSeconds = r.ReadSingle();
            v.RecordingCandidates = r.ReadInt32();
            v.Profiles = ReadList(r, reader => ReadLineProfile(reader));
            return v;
        }

        internal static void WriteLineView(BinaryWriter w, LineView v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.WeekId);
            w.Write(v.StateVersion);
            w.Write(v.Me);
            w.Write(v.Now);
            w.Write((byte)v.Phase);
            w.Write(v.PhaseStartTime);
            w.Write(v.PhaseEndTime);
            w.Write(v.Day);
            w.Write(v.DayCount);
            w.Write((byte)v.Length);
            w.Write(v.FirstWeek);
            w.Write(v.RoundIndex);
            WriteList(w, v.TodayRounds, (writer, item) => { writer.Write((byte)item); });
            WriteString(w, v.ManagerProfileId);
            w.Write(v.Favourite);
            WriteLineFeaturesView(w, v.Features);
            WriteList(w, v.Players, (writer, item) => { WriteLinePlayerView(writer, item); });
            WriteLineMeView(w, v.Mine);
            WriteLineRoundView(w, v.Round);
            WriteList(w, v.TodayReveals, (writer, item) => { WriteLineRoundReveal(writer, item); });
            WriteLineMeetingView(w, v.Meeting);
            WriteLineWeekResult(w, v.Result);
        }

        internal static LineView ReadLineView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineView();
            v.WeekId = r.ReadInt32();
            v.StateVersion = r.ReadInt64();
            v.Me = r.ReadInt32();
            v.Now = r.ReadDouble();
            v.Phase = (LinePhase)ReadEnum(r, 13);
            v.PhaseStartTime = r.ReadDouble();
            v.PhaseEndTime = r.ReadDouble();
            v.Day = r.ReadInt32();
            v.DayCount = r.ReadInt32();
            v.Length = (WeekLength)ReadEnum(r, 1);
            v.FirstWeek = ReadBool(r);
            v.RoundIndex = r.ReadInt32();
            v.TodayRounds = ReadList(r, reader => (RoundType)ReadEnum(reader, 6));
            v.ManagerProfileId = ReadString(r);
            v.Favourite = r.ReadInt32();
            v.Features = ReadLineFeaturesView(r);
            v.Players = ReadList(r, reader => ReadLinePlayerView(reader));
            v.Mine = ReadLineMeView(r);
            v.Round = ReadLineRoundView(r);
            v.TodayReveals = ReadList(r, reader => ReadLineRoundReveal(reader));
            v.Meeting = ReadLineMeetingView(r);
            v.Result = ReadLineWeekResult(r);
            return v;
        }

        internal static void WriteLineFeaturesView(BinaryWriter w, LineFeaturesView v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.TricksUnlocked);
            w.Write(v.TipLineUnlocked);
            w.Write(v.RecordingsEnabled);
            w.Write(v.IsLastDay);
            w.Write(v.FirstTimeRound);
            w.Write(v.OnboardingDay);
        }

        internal static LineFeaturesView ReadLineFeaturesView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineFeaturesView();
            v.TricksUnlocked = ReadBool(r);
            v.TipLineUnlocked = ReadBool(r);
            v.RecordingsEnabled = ReadBool(r);
            v.IsLastDay = ReadBool(r);
            v.FirstTimeRound = ReadBool(r);
            v.OnboardingDay = ReadBool(r);
            return v;
        }

        internal static void WriteLinePlayerView(BinaryWriter w, LinePlayerView v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.Id);
            WriteString(w, v.Name);
            w.Write(v.ColorIndex);
            w.Write(v.Connected);
            w.Write(v.IsBot);
            w.Write(v.Blame);
            w.Write(v.Ihtars);
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
            v.Ihtars = r.ReadInt32();
            v.Submitted = ReadBool(r);
            v.Voted = ReadBool(r);
            v.SessionPoints = r.ReadInt32();
            return v;
        }

        internal static void WriteLineMeView(BinaryWriter w, LineMeView v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.AgendaTarget);
            w.Write((byte)v.Trick);
            w.Write(v.TrickUsedToday);
            w.Write(v.TipUsedToday);
            w.Write(v.TricksLeftThisWeek);
            w.Write(v.TipsLeftThisWeek);
            w.Write(v.MailsSentToday);
            w.Write(v.ComputerBlockedUntil);
            w.Write((byte)v.ComputerBlockedBy);
            w.Write(v.PhoneDeadUntil);
            w.Write(v.WiretapTarget);
            w.Write(v.WiretapUntil);
            WriteLinePhoneView(w, v.Phone);
            WriteList(w, v.Inbox, (writer, item) => { WriteLineMailView(writer, item); });
            WriteList(w, v.CallLog, (writer, item) => { WriteLineCallRecordView(writer, item); });
        }

        internal static LineMeView ReadLineMeView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineMeView();
            v.AgendaTarget = r.ReadInt32();
            v.Trick = (TrickType)ReadEnum(r, 6);
            v.TrickUsedToday = ReadBool(r);
            v.TipUsedToday = ReadBool(r);
            v.TricksLeftThisWeek = r.ReadInt32();
            v.TipsLeftThisWeek = r.ReadInt32();
            v.MailsSentToday = r.ReadInt32();
            v.ComputerBlockedUntil = r.ReadDouble();
            v.ComputerBlockedBy = (TrickType)ReadEnum(r, 6);
            v.PhoneDeadUntil = r.ReadDouble();
            v.WiretapTarget = r.ReadInt32();
            v.WiretapUntil = r.ReadDouble();
            v.Phone = ReadLinePhoneView(r);
            v.Inbox = ReadList(r, reader => ReadLineMailView(reader));
            v.CallLog = ReadList(r, reader => ReadLineCallRecordView(reader));
            return v;
        }

        internal static void WriteLinePhoneView(BinaryWriter w, LinePhoneView v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write((byte)v.Status);
            w.Write(v.Other);
            w.Write(v.CallId);
            w.Write(v.Since);
            w.Write(v.RingUntil);
        }

        internal static LinePhoneView ReadLinePhoneView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LinePhoneView();
            v.Status = (CallStatus)ReadEnum(r, 3);
            v.Other = r.ReadInt32();
            v.CallId = r.ReadInt32();
            v.Since = r.ReadDouble();
            v.RingUntil = r.ReadDouble();
            return v;
        }

        internal static void WriteLineMailView(BinaryWriter w, LineMailView v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.Id);
            w.Write((byte)v.Kind);
            w.Write(v.From);
            w.Write(v.TemplateId);
            w.Write(v.About);
            w.Write(v.ToAll);
            w.Write(v.Time);
        }

        internal static LineMailView ReadLineMailView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineMailView();
            v.Id = r.ReadInt32();
            v.Kind = (MailKind)ReadEnum(r, 2);
            v.From = r.ReadInt32();
            v.TemplateId = r.ReadInt32();
            v.About = r.ReadInt32();
            v.ToAll = ReadBool(r);
            v.Time = r.ReadDouble();
            return v;
        }

        internal static void WriteLineCallRecordView(BinaryWriter w, LineCallRecordView v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.CallId);
            w.Write(v.Other);
            w.Write(v.Outgoing);
            w.Write(v.Connected);
            w.Write(v.Start);
            w.Write(v.End);
            w.Write((byte)v.EndReason);
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
            v.EndReason = (CallEndReason)ReadEnum(r, 6);
            return v;
        }

        internal static void WriteLineRoundView(BinaryWriter w, LineRoundView v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write((byte)v.Type);
            w.Write(v.Index);
            w.Write(v.PlayEndTime);
            w.Write(v.Partner);
            WriteList(w, v.Partners, (writer, item) => { writer.Write(item); });
            WriteList(w, v.NoAlibi, (writer, item) => { writer.Write(item); });
            w.Write(v.NeighborPosition);
            w.Write(v.NeighborDigit);
            w.Write(v.TopicId);
            w.Write(v.TopicVariant);
            w.Write(v.IsOdd);
            WriteList(w, v.GuessOptions, (writer, item) => { writer.Write(item); });
            w.Write(v.IsCulprit);
            w.Write(v.ClueInnocent);
            w.Write(v.DigitPosition);
            w.Write(v.Digit);
            w.Write(v.IsSaboteur);
            WriteList(w, v.PositionOwners, (writer, item) => { writer.Write(item); });
            w.Write(v.CodeLength);
            WriteList(w, v.Attempts, (writer, item) => { WriteLineAttemptView(writer, item); });
            w.Write(v.Solved);
            w.Write(v.Submitted);
            w.Write(v.MyTarget);
            w.Write(v.MyA);
            w.Write(v.AttemptsLeft);
            WriteLineRoundReveal(w, v.Reveal);
        }

        internal static LineRoundView ReadLineRoundView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineRoundView();
            v.Type = (RoundType)ReadEnum(r, 6);
            v.Index = r.ReadInt32();
            v.PlayEndTime = r.ReadDouble();
            v.Partner = r.ReadInt32();
            v.Partners = ReadList(r, reader => reader.ReadInt32());
            v.NoAlibi = ReadList(r, reader => reader.ReadInt32());
            v.NeighborPosition = r.ReadInt32();
            v.NeighborDigit = r.ReadInt32();
            v.TopicId = r.ReadInt32();
            v.TopicVariant = r.ReadInt32();
            v.IsOdd = ReadBool(r);
            v.GuessOptions = ReadList(r, reader => reader.ReadInt32());
            v.IsCulprit = ReadBool(r);
            v.ClueInnocent = r.ReadInt32();
            v.DigitPosition = r.ReadInt32();
            v.Digit = r.ReadInt32();
            v.IsSaboteur = ReadBool(r);
            v.PositionOwners = ReadList(r, reader => reader.ReadInt32());
            v.CodeLength = r.ReadInt32();
            v.Attempts = ReadList(r, reader => ReadLineAttemptView(reader));
            v.Solved = ReadBool(r);
            v.Submitted = ReadBool(r);
            v.MyTarget = r.ReadInt32();
            v.MyA = r.ReadInt32();
            v.AttemptsLeft = r.ReadInt32();
            v.Reveal = ReadLineRoundReveal(r);
            return v;
        }

        internal static void WriteLineAttemptView(BinaryWriter w, LineAttemptView v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.Player);
            w.Write(v.Code);
            w.Write(v.Correct);
            w.Write(v.Time);
        }

        internal static LineAttemptView ReadLineAttemptView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineAttemptView();
            v.Player = r.ReadInt32();
            v.Code = r.ReadInt32();
            v.Correct = r.ReadInt32();
            v.Time = r.ReadDouble();
            return v;
        }

        internal static void WriteLineRoundReveal(BinaryWriter w, LineRoundReveal v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write((byte)v.Type);
            w.Write(v.Index);
            WriteList(w, v.Pairs, (writer, item) => { WriteLinePairReveal(writer, item); });
            w.Write(v.Hidden);
            w.Write(v.TopicId);
            w.Write(v.OddGuess);
            w.Write(v.OddGuessCorrect);
            WriteList(w, v.Clues, (writer, item) => { writer.Write(item); });
            WriteList(w, v.Votes, (writer, item) => { WriteLineVoteView(writer, item); });
            WriteList(w, v.Counts, (writer, item) => { writer.Write(item); });
            WriteList(w, v.Complaints, (writer, item) => { WriteLineComplaintView(writer, item); });
            w.Write(v.Winner);
            w.Write(v.Code);
            w.Write(v.Solved);
            w.Write(v.SolvedBy);
            WriteList(w, v.CodesByPlayer, (writer, item) => { writer.Write(item); });
            WriteList(w, v.Deltas, (writer, item) => { WriteLineBlameDelta(writer, item); });
        }

        internal static LineRoundReveal ReadLineRoundReveal(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineRoundReveal();
            v.Type = (RoundType)ReadEnum(r, 6);
            v.Index = r.ReadInt32();
            v.Pairs = ReadList(r, reader => ReadLinePairReveal(reader));
            v.Hidden = r.ReadInt32();
            v.TopicId = r.ReadInt32();
            v.OddGuess = r.ReadInt32();
            v.OddGuessCorrect = ReadBool(r);
            v.Clues = ReadList(r, reader => reader.ReadInt32());
            v.Votes = ReadList(r, reader => ReadLineVoteView(reader));
            v.Counts = ReadList(r, reader => reader.ReadInt32());
            v.Complaints = ReadList(r, reader => ReadLineComplaintView(reader));
            v.Winner = r.ReadInt32();
            v.Code = r.ReadInt32();
            v.Solved = ReadBool(r);
            v.SolvedBy = r.ReadInt32();
            v.CodesByPlayer = ReadList(r, reader => reader.ReadInt32());
            v.Deltas = ReadList(r, reader => ReadLineBlameDelta(reader));
            return v;
        }

        internal static void WriteLinePairReveal(BinaryWriter w, LinePairReveal v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.A);
            w.Write(v.B);
            w.Write(v.ChoiceA);
            w.Write(v.ChoiceB);
            w.Write(v.C);
            w.Write(v.ChoiceC);
        }

        internal static LinePairReveal ReadLinePairReveal(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LinePairReveal();
            v.A = r.ReadInt32();
            v.B = r.ReadInt32();
            v.ChoiceA = r.ReadInt32();
            v.ChoiceB = r.ReadInt32();
            v.C = r.ReadInt32();
            v.ChoiceC = r.ReadInt32();
            return v;
        }

        internal static void WriteLineVoteView(BinaryWriter w, LineVoteView v)
        {
            w.Write(v != null);
            if (v == null) return;
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

        internal static void WriteLineComplaintView(BinaryWriter w, LineComplaintView v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.Target);
            w.Write(v.Reason);
        }

        internal static LineComplaintView ReadLineComplaintView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineComplaintView();
            v.Target = r.ReadInt32();
            v.Reason = r.ReadInt32();
            return v;
        }

        internal static void WriteLineBlameDelta(BinaryWriter w, LineBlameDelta v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.Player);
            w.Write(v.Delta);
            w.Write((byte)v.Reason);
            w.Write((byte)v.Round);
            w.Write((byte)v.Trick);
            w.Write(v.Day);
        }

        internal static LineBlameDelta ReadLineBlameDelta(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineBlameDelta();
            v.Player = r.ReadInt32();
            v.Delta = r.ReadInt32();
            v.Reason = (BlameReason)ReadEnum(r, 5);
            v.Round = (RoundType)ReadEnum(r, 6);
            v.Trick = (TrickType)ReadEnum(r, 6);
            v.Day = r.ReadInt32();
            return v;
        }

        internal static void WriteLineMeetingView(BinaryWriter w, LineMeetingView v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.IsFinal);
            WriteList(w, v.Dossier, (writer, item) => { WriteLineDossierEntry(writer, item); });
            WriteList(w, v.Recordings, (writer, item) => { WriteLineRecordingPick(writer, item); });
            w.Write(v.MyVote);
            w.Write(v.IhtarTo);
            WriteList(w, v.Votes, (writer, item) => { WriteLineVoteView(writer, item); });
            WriteList(w, v.Shortlist, (writer, item) => { writer.Write(item); });
            WriteList(w, v.DefenseOrder, (writer, item) => { writer.Write(item); });
            w.Write(v.Defender);
            w.Write(v.DefenderEndTime);
            w.Write(v.Fired);
            WriteList(w, v.Scores, (writer, item) => { WriteLineFireScore(writer, item); });
        }

        internal static LineMeetingView ReadLineMeetingView(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineMeetingView();
            v.IsFinal = ReadBool(r);
            v.Dossier = ReadList(r, reader => ReadLineDossierEntry(reader));
            v.Recordings = ReadList(r, reader => ReadLineRecordingPick(reader));
            v.MyVote = r.ReadInt32();
            v.IhtarTo = r.ReadInt32();
            v.Votes = ReadList(r, reader => ReadLineVoteView(reader));
            v.Shortlist = ReadList(r, reader => reader.ReadInt32());
            v.DefenseOrder = ReadList(r, reader => reader.ReadInt32());
            v.Defender = r.ReadInt32();
            v.DefenderEndTime = r.ReadDouble();
            v.Fired = r.ReadInt32();
            v.Scores = ReadList(r, reader => ReadLineFireScore(reader));
            return v;
        }

        internal static void WriteLineDossierEntry(BinaryWriter w, LineDossierEntry v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write((byte)v.Kind);
            w.Write((byte)v.Round);
            w.Write((byte)v.Trick);
            w.Write(v.Player);
            w.Write(v.Victim);
            w.Write(v.TemplateId);
            w.Write(v.Named);
            w.Write(v.Arg);
        }

        internal static LineDossierEntry ReadLineDossierEntry(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineDossierEntry();
            v.Kind = (DossierKind)ReadEnum(r, 7);
            v.Round = (RoundType)ReadEnum(r, 6);
            v.Trick = (TrickType)ReadEnum(r, 6);
            v.Player = r.ReadInt32();
            v.Victim = r.ReadInt32();
            v.TemplateId = r.ReadInt32();
            v.Named = ReadBool(r);
            v.Arg = r.ReadInt32();
            return v;
        }

        internal static void WriteLineRecordingPick(BinaryWriter w, LineRecordingPick v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.Index);
            WriteList(w, v.CandidateCallIds, (writer, item) => { writer.Write(item); });
            w.Write(v.PlayAt);
            w.Write(v.Seconds);
            w.Write(v.ChosenCallId);
            w.Write(v.Caller);
            w.Write(v.Callee);
            w.Write(v.CallStart);
            w.Write(v.Played);
        }

        internal static LineRecordingPick ReadLineRecordingPick(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineRecordingPick();
            v.Index = r.ReadInt32();
            v.CandidateCallIds = ReadList(r, reader => reader.ReadInt32());
            v.PlayAt = r.ReadDouble();
            v.Seconds = r.ReadSingle();
            v.ChosenCallId = r.ReadInt32();
            v.Caller = r.ReadInt32();
            v.Callee = r.ReadInt32();
            v.CallStart = r.ReadDouble();
            v.Played = ReadBool(r);
            return v;
        }

        internal static void WriteLineFireScore(BinaryWriter w, LineFireScore v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.Player);
            w.Write(v.Blame);
            w.Write(v.Votes);
            w.Write(v.Ihtars);
            w.Write(v.Score);
        }

        internal static LineFireScore ReadLineFireScore(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineFireScore();
            v.Player = r.ReadInt32();
            v.Blame = r.ReadInt32();
            v.Votes = r.ReadInt32();
            v.Ihtars = r.ReadInt32();
            v.Score = r.ReadInt32();
            return v;
        }

        internal static void WriteLineWeekResult(BinaryWriter w, LineWeekResult v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.WeekId);
            WriteString(w, v.ManagerProfileId);
            w.Write(v.Days);
            w.Write(v.Fired);
            WriteString(w, v.NextManagerPick);
            WriteList(w, v.Players, (writer, item) => { WriteLineWeekPlayerResult(writer, item); });
            w.Write(v.ResultHash);
        }

        internal static LineWeekResult ReadLineWeekResult(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineWeekResult();
            v.WeekId = r.ReadInt32();
            v.ManagerProfileId = ReadString(r);
            v.Days = r.ReadInt32();
            v.Fired = r.ReadInt32();
            v.NextManagerPick = ReadString(r);
            v.Players = ReadList(r, reader => ReadLineWeekPlayerResult(reader));
            v.ResultHash = r.ReadUInt64();
            return v;
        }

        internal static void WriteLineWeekPlayerResult(BinaryWriter w, LineWeekPlayerResult v)
        {
            w.Write(v != null);
            if (v == null) return;
            w.Write(v.Player);
            w.Write(v.Blame);
            w.Write(v.Ihtars);
            w.Write(v.Survived);
            w.Write(v.AgendaTarget);
            w.Write(v.AgendaHit);
            w.Write(v.AgendaIhtars);
            w.Write(v.EmployeeOfWeek);
            w.Write((byte)v.Trick);
            w.Write(v.TricksUsed);
            w.Write(v.Points);
            w.Write(v.SessionPoints);
        }

        internal static LineWeekPlayerResult ReadLineWeekPlayerResult(BinaryReader r)
        {
            if (!ReadBool(r)) return null;
            var v = new LineWeekPlayerResult();
            v.Player = r.ReadInt32();
            v.Blame = r.ReadInt32();
            v.Ihtars = r.ReadInt32();
            v.Survived = ReadBool(r);
            v.AgendaTarget = r.ReadInt32();
            v.AgendaHit = ReadBool(r);
            v.AgendaIhtars = r.ReadInt32();
            v.EmployeeOfWeek = ReadBool(r);
            v.Trick = (TrickType)ReadEnum(r, 6);
            v.TricksUsed = r.ReadInt32();
            v.Points = r.ReadInt32();
            v.SessionPoints = r.ReadInt32();
            return v;
        }
    }
}
