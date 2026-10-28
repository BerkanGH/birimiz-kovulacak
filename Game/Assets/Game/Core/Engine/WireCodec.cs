using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BirimizKovulacak.Core
{
    /// <summary>
    /// Versioned little-endian messages. Field order is explicit and matches the contract declarations;
    /// enum values occupy one byte. Strings and lists use a compact unsigned length (zero means null,
    /// otherwise count + 1); reference objects use a presence byte. No runtime reflection is used.
    /// </summary>
    public static class WireCodec
    {
        public static byte[] EncodeView(PlayerView view) { return Encode(1, w => WritePlayerView(w, view)); }
        public static PlayerView DecodeView(byte[] data) { return Decode(data, 1, r => ReadPlayerView(r)); }

        public static byte[] EncodeRequest(ActionRequest request) { return Encode(2, w => WriteActionRequest(w, request)); }
        public static ActionRequest DecodeRequest(byte[] data) { return Decode(data, 2, r => ReadActionRequest(r)); }

        public static byte[] EncodeResult(ActionResult result) { return Encode(3, w => WriteActionResult(w, result)); }
        public static ActionResult DecodeResult(byte[] data) { return Decode(data, 3, r => ReadActionResult(r)); }

        public static byte[] EncodeEvents(List<GameEvent> events) { return Encode(4, w => WriteList(w, events, (writer, item) => WriteGameEvent(writer, item))); }
        public static List<GameEvent> DecodeEvents(byte[] data) { return Decode(data, 4, r => ReadList(r, reader => ReadGameEvent(reader))); }

        public static byte[] EncodeLobby(LobbyView lobby) { return Encode(5, w => WriteLobbyView(w, lobby)); }
        public static LobbyView DecodeLobby(byte[] data) { return Decode(data, 5, r => ReadLobbyView(r)); }

        public static byte[] EncodeConnectionPayload(ConnectionPayload payload) { return Encode(6, w => WriteConnectionPayload(w, payload)); }
        public static ConnectionPayload DecodeConnectionPayload(byte[] data) { return Decode(data, 6, r => ReadConnectionPayload(r)); }

        public static byte[] EncodeWelcome(WelcomeMessage welcome) { return Encode(7, w => WriteWelcomeMessage(w, welcome)); }
        public static WelcomeMessage DecodeWelcome(byte[] data) { return Decode(data, 7, r => ReadWelcomeMessage(r)); }

        /// <summary>FNV-1a 64 over the result encoding with ResultHash blank, without mutating the result.</summary>
        public static string HashResult(MatchResultView result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            byte[] bytes = Encode(8, w => WriteMatchResultView(w, result, true));
            ulong hash = 14695981039346656037UL;
            unchecked
            {
                for (int i = 0; i < bytes.Length; i++) hash = (hash ^ bytes[i]) * 1099511628211UL;
            }
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        private static byte[] Encode(byte tag, Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true))
            {
                writer.Write(tag);
                writer.Write((ushort)Protocol.Version);
                write(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static T Decode<T>(byte[] data, byte expectedTag, Func<BinaryReader, T> read)
        {
            if (data == null) throw new FormatException("Missing message.");
            try
            {
                using (var stream = new MemoryStream(data, false))
                using (var reader = new BinaryReader(stream, new UTF8Encoding(false, true), true))
                {
                    if (reader.ReadByte() != expectedTag || reader.ReadUInt16() != Protocol.Version)
                        throw new FormatException("Message tag or protocol version mismatch.");
                    T result = read(reader);
                    if (stream.Position != stream.Length) throw new FormatException("Trailing message data.");
                    return result;
                }
            }
            catch (EndOfStreamException e) { throw new FormatException("Truncated message.", e); }
            catch (IOException e) { throw new FormatException("Invalid message.", e); }
            catch (ArgumentException e) { throw new FormatException("Invalid message encoding.", e); }
            catch (OverflowException e) { throw new FormatException("Invalid message length.", e); }
        }

        private static bool ReadBoolean(BinaryReader r)
        {
            byte value = r.ReadByte();
            if (value > 1) throw new FormatException("Invalid boolean.");
            return value != 0;
        }

        private static void WriteLength(BinaryWriter w, int count)
        {
            uint value = checked((uint)(count + 1));
            while (value >= 128) { w.Write((byte)((value & 127) | 128)); value >>= 7; }
            w.Write((byte)value);
        }

        private static int ReadLength(BinaryReader r)
        {
            uint value = 0;
            for (int shift = 0; shift <= 28; shift += 7)
            {
                byte part = r.ReadByte();
                if (shift == 28 && (part & 248) != 0) throw new FormatException("Length overflow.");
                value |= (uint)(part & 127) << shift;
                if ((part & 128) == 0)
                {
                    if (shift > 0 && part == 0) throw new FormatException("Noncanonical length.");
                    int count = checked((int)value) - 1;
                    if (count > r.BaseStream.Length - r.BaseStream.Position)
                        throw new FormatException("Length exceeds remaining message.");
                    return count;
                }
            }
            throw new FormatException("Unterminated length.");
        }

        private static void WriteString(BinaryWriter w, string value)
        {
            if (value == null) { WriteLength(w, -1); return; }
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(value);
            WriteLength(w, bytes.Length);
            w.Write(bytes);
        }

        private static string ReadString(BinaryReader r)
        {
            int count = ReadLength(r);
            if (count < 0) return null;
            return new UTF8Encoding(false, true).GetString(r.ReadBytes(count));
        }

        private static void WriteList<T>(BinaryWriter w, List<T> values, Action<BinaryWriter, T> write)
        {
            if (values == null) { WriteLength(w, -1); return; }
            WriteLength(w, values.Count);
            for (int i = 0; i < values.Count; i++) write(w, values[i]);
        }

        private static List<T> ReadList<T>(BinaryReader r, Func<BinaryReader, T> read)
        {
            int count = ReadLength(r);
            if (count < 0) return null;
            var result = new List<T>();
            for (int i = 0; i < count; i++) result.Add(read(r));
            return result;
        }

        private static void WriteVec3(BinaryWriter w, Vec3 value)
        {
            w.Write(value.X); w.Write(value.Y); w.Write(value.Z);
        }

        private static Vec3 ReadVec3(BinaryReader r)
        {
            return new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        }

        private static void WritePlayerView(BinaryWriter w, PlayerView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.MatchId);
            w.Write(value.StateVersion);
            w.Write(value.Recipient);
            w.Write((byte)value.Phase);
            w.Write(value.PhaseSerial);
            w.Write(value.PhaseStartTime);
            w.Write(value.PhaseEndTime);
            w.Write(value.ServerTime);
            w.Write((byte)value.AbandonReason);
            WriteQuotaView(w, value.Quota);
            WriteList(w, value.Players, (writer, item) => WritePlayerPublicView(writer, item));
            WriteList(w, value.Items, (writer, item) => WriteItemView(writer, item));
            WriteList(w, value.Desks, (writer, item) => WriteDeskView(writer, item));
            WriteList(w, value.Stations, (writer, item) => WriteStationView(writer, item));
            WriteList(w, value.Evidence, (writer, item) => WriteEvidenceView(writer, item));
            WriteList(w, value.Observations, (writer, item) => WriteObservationView(writer, item));
            WriteList(w, value.Log, (writer, item) => WriteLogEntry(writer, item));
            WriteManagerView(w, value.Manager);
            WriteMeetingView(w, value.Meeting);
            WritePrivateView(w, value.Me);
            WriteMatchResultView(w, value.Result);
            w.Write((byte)value.Mode);
            w.Write(value.WeekDay);
            WriteString(w, value.ManagerProfileId);
            w.Write(value.Favourite);
            WriteFeaturesView(w, value.Features);
            WriteOfficeStatusView(w, value.Office);
            WriteList(w, value.Inbox, (writer, item) => WriteEmailView(writer, item));
        }

        private static PlayerView ReadPlayerView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new PlayerView
            {
                MatchId = r.ReadInt32(),
                StateVersion = r.ReadInt32(),
                Recipient = r.ReadInt32(),
                Phase = (MatchPhase)r.ReadByte(),
                PhaseSerial = r.ReadInt32(),
                PhaseStartTime = r.ReadDouble(),
                PhaseEndTime = r.ReadDouble(),
                ServerTime = r.ReadDouble(),
                AbandonReason = (AbandonReason)r.ReadByte(),
                Quota = ReadQuotaView(r),
                Players = ReadList(r, reader => ReadPlayerPublicView(reader)),
                Items = ReadList(r, reader => ReadItemView(reader)),
                Desks = ReadList(r, reader => ReadDeskView(reader)),
                Stations = ReadList(r, reader => ReadStationView(reader)),
                Evidence = ReadList(r, reader => ReadEvidenceView(reader)),
                Observations = ReadList(r, reader => ReadObservationView(reader)),
                Log = ReadList(r, reader => ReadLogEntry(reader)),
                Manager = ReadManagerView(r),
                Meeting = ReadMeetingView(r),
                Me = ReadPrivateView(r),
                Result = ReadMatchResultView(r),
                Mode = (GameMode)r.ReadByte(),
                WeekDay = r.ReadInt32(),
                ManagerProfileId = ReadString(r),
                Favourite = r.ReadInt32(),
                Features = ReadFeaturesView(r),
                Office = ReadOfficeStatusView(r),
                Inbox = ReadList(r, reader => ReadEmailView(reader)),
            };
        }

        private static void WriteQuotaView(BinaryWriter w, QuotaView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Points);
            w.Write(value.Target);
            w.Write(value.CoffeeOrders);
            w.Write(value.ReportOrders);
            w.Write(value.PhoneRinging);
            w.Write(value.PhoneRingEnds);
            w.Write(value.DirtyDesks);
            w.Write(value.Frozen);
            w.Write(value.OpenMail);
        }

        private static QuotaView ReadQuotaView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new QuotaView
            {
                Points = r.ReadSingle(),
                Target = r.ReadSingle(),
                CoffeeOrders = r.ReadInt32(),
                ReportOrders = r.ReadInt32(),
                PhoneRinging = ReadBoolean(r),
                PhoneRingEnds = r.ReadDouble(),
                DirtyDesks = r.ReadInt32(),
                Frozen = ReadBoolean(r),
                OpenMail = r.ReadInt32(),
            };
        }

        private static void WritePlayerPublicView(BinaryWriter w, PlayerPublicView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Id);
            WriteString(w, value.Name);
            w.Write(value.ColorIndex);
            w.Write(value.DeskIndex);
            w.Write(value.Connected);
            w.Write(value.DisconnectDeadline);
            w.Write(value.Performance);
            w.Write(value.TasksCompleted);
            w.Write(value.SuspicionLevel);
            WriteString(w, value.CarryingItemId);
            w.Write((byte)value.Activity);
            WriteString(w, value.ActivityTargetId);
            w.Write(value.ActivityStart);
            w.Write(value.ActivityEnd);
            w.Write(value.WorkPose);
            w.Write(value.AccusationsReceived);
            w.Write(value.Crouched);
            w.Write(value.Ringing);
            w.Write(value.Favor);
            w.Write(value.Emote);
            w.Write(value.EmoteUntil);
            w.Write(value.CareerPoints);
        }

        private static PlayerPublicView ReadPlayerPublicView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new PlayerPublicView
            {
                Id = r.ReadInt32(),
                Name = ReadString(r),
                ColorIndex = r.ReadInt32(),
                DeskIndex = r.ReadInt32(),
                Connected = ReadBoolean(r),
                DisconnectDeadline = r.ReadDouble(),
                Performance = r.ReadSingle(),
                TasksCompleted = r.ReadInt32(),
                SuspicionLevel = r.ReadInt32(),
                CarryingItemId = ReadString(r),
                Activity = (PublicActivity)r.ReadByte(),
                ActivityTargetId = ReadString(r),
                ActivityStart = r.ReadDouble(),
                ActivityEnd = r.ReadDouble(),
                WorkPose = ReadBoolean(r),
                AccusationsReceived = r.ReadInt32(),
                Crouched = ReadBoolean(r),
                Ringing = ReadBoolean(r),
                Favor = r.ReadSingle(),
                Emote = r.ReadInt32(),
                EmoteUntil = r.ReadDouble(),
                CareerPoints = r.ReadInt32(),
            };
        }

        private static void WriteItemView(BinaryWriter w, ItemView value)
        {
            w.Write(value != null);
            if (value == null) return;
            WriteString(w, value.Id);
            w.Write((byte)value.Kind);
            WriteString(w, value.Variant);
            w.Write((byte)value.Location);
            WriteString(w, value.SlotId);
            w.Write(value.SlotIndex);
            w.Write(value.CarrierId);
            WriteVec3(w, value.FloorPosition);
            w.Write(value.FloorYaw);
            w.Write((byte)value.Fill);
            w.Write(value.ReportOwner);
            w.Write(value.NameplateOf);
            w.Write(value.Sealed);
            w.Write(value.IsEvidence);
            w.Write(value.AddressedDesk);
            w.Write(value.KnownSalted);
        }

        private static ItemView ReadItemView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new ItemView
            {
                Id = ReadString(r),
                Kind = (ItemKind)r.ReadByte(),
                Variant = ReadString(r),
                Location = (ItemLocationKind)r.ReadByte(),
                SlotId = ReadString(r),
                SlotIndex = r.ReadInt32(),
                CarrierId = r.ReadInt32(),
                FloorPosition = ReadVec3(r),
                FloorYaw = r.ReadSingle(),
                Fill = (MugFill)r.ReadByte(),
                ReportOwner = r.ReadInt32(),
                NameplateOf = r.ReadInt32(),
                Sealed = ReadBoolean(r),
                IsEvidence = ReadBoolean(r),
                AddressedDesk = r.ReadInt32(),
                KnownSalted = ReadBoolean(r),
            };
        }

        private static void WriteDeskView(BinaryWriter w, DeskView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Index);
            WriteString(w, value.Id);
            w.Write(value.AssignedOwner);
            w.Write(value.DisplayedNameplateOf);
            w.Write(value.Dirty);
            w.Write(value.Spill);
            w.Write(value.PcLocked);
            w.Write(value.PcLockedAt);
            w.Write(value.PcLockedUntil);
            w.Write(value.ClutterCount);
            w.Write(value.ScreenOpen);
            w.Write(value.ScreenOpenUntil);
            w.Write(value.Unplugged);
            w.Write(value.PcImmuneUntil);
        }

        private static DeskView ReadDeskView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new DeskView
            {
                Index = r.ReadInt32(),
                Id = ReadString(r),
                AssignedOwner = r.ReadInt32(),
                DisplayedNameplateOf = r.ReadInt32(),
                Dirty = ReadBoolean(r),
                Spill = ReadBoolean(r),
                PcLocked = ReadBoolean(r),
                PcLockedAt = r.ReadDouble(),
                PcLockedUntil = r.ReadDouble(),
                ClutterCount = r.ReadInt32(),
                ScreenOpen = ReadBoolean(r),
                ScreenOpenUntil = r.ReadDouble(),
                Unplugged = ReadBoolean(r),
                PcImmuneUntil = r.ReadDouble(),
            };
        }

        private static void WriteStationView(BinaryWriter w, StationView value)
        {
            w.Write(value != null);
            if (value == null) return;
            WriteString(w, value.Id);
            w.Write((byte)value.Kind);
            w.Write(value.UsedBy);
            w.Write(value.BusyUntil);
            WriteList(w, value.PrintQueue, (writer, item) => writer.Write(item));
            w.Write(value.PrintEndsAt);
        }

        private static StationView ReadStationView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new StationView
            {
                Id = ReadString(r),
                Kind = (StationKind)r.ReadByte(),
                UsedBy = r.ReadInt32(),
                BusyUntil = r.ReadDouble(),
                PrintQueue = ReadList(r, reader => reader.ReadInt32()),
                PrintEndsAt = r.ReadDouble(),
            };
        }

        private static void WriteEvidenceView(BinaryWriter w, EvidenceView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Id);
            w.Write((byte)value.Kind);
            w.Write(value.Time);
            w.Write((byte)value.PhaseFound);
            WriteString(w, value.ItemId);
            WriteString(w, value.ItemVariant);
            WriteString(w, value.LocationId);
            w.Write(value.DeskIndex);
            w.Write(value.AttributedTo);
            w.Write((byte)value.Source);
            w.Write(value.SourcePlayer);
            w.Write((byte)value.Status);
            w.Write(value.Sealed);
            w.Write(value.SubjectPlayer);
            w.Write((byte)value.ObservedActivity);
            WriteString(w, value.HeldBefore);
            WriteString(w, value.HeldAfter);
            w.Write((byte)value.ScanReliability);
            w.Write((byte)value.ScanSignal);
            w.Write(value.RiskWeight);
            WriteList(w, value.Contents, (writer, item) => WriteString(writer, item));
            w.Write(value.EmailTemplate);
            WriteString(w, value.CameraId);
            w.Write(value.WindowStart);
            w.Write(value.WindowEnd);
            w.Write(value.NoRecording);
            WriteList(w, value.Sightings, (writer, item) => WriteFootageSighting(writer, item));
            WriteList(w, value.Calls, (writer, item) => WriteCallRecordView(writer, item));
            w.Write(value.Salted);
        }

        private static EvidenceView ReadEvidenceView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new EvidenceView
            {
                Id = r.ReadInt32(),
                Kind = (EvidenceKind)r.ReadByte(),
                Time = r.ReadDouble(),
                PhaseFound = (MatchPhase)r.ReadByte(),
                ItemId = ReadString(r),
                ItemVariant = ReadString(r),
                LocationId = ReadString(r),
                DeskIndex = r.ReadInt32(),
                AttributedTo = r.ReadInt32(),
                Source = (DiscoverySource)r.ReadByte(),
                SourcePlayer = r.ReadInt32(),
                Status = (EvidenceStatus)r.ReadByte(),
                Sealed = ReadBoolean(r),
                SubjectPlayer = r.ReadInt32(),
                ObservedActivity = (PublicActivity)r.ReadByte(),
                HeldBefore = ReadString(r),
                HeldAfter = ReadString(r),
                ScanReliability = (Reliability)r.ReadByte(),
                ScanSignal = (ScanSignal)r.ReadByte(),
                RiskWeight = r.ReadSingle(),
                Contents = ReadList(r, reader => ReadString(reader)),
                EmailTemplate = r.ReadInt32(),
                CameraId = ReadString(r),
                WindowStart = r.ReadDouble(),
                WindowEnd = r.ReadDouble(),
                NoRecording = ReadBoolean(r),
                Sightings = ReadList(r, reader => ReadFootageSighting(reader)),
                Calls = ReadList(r, reader => ReadCallRecordView(reader)),
                Salted = ReadBoolean(r),
            };
        }

        private static void WriteObservationView(BinaryWriter w, ObservationView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Id);
            w.Write((byte)value.Kind);
            w.Write(value.Time);
            w.Write((byte)value.Phase);
            w.Write(value.Subject);
            w.Write(value.DeskIndex);
            w.Write(value.Severity);
            WriteString(w, value.Detail);
        }

        private static ObservationView ReadObservationView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new ObservationView
            {
                Id = r.ReadInt32(),
                Kind = (ObservationKind)r.ReadByte(),
                Time = r.ReadDouble(),
                Phase = (MatchPhase)r.ReadByte(),
                Subject = r.ReadInt32(),
                DeskIndex = r.ReadInt32(),
                Severity = r.ReadSingle(),
                Detail = ReadString(r),
            };
        }

        private static void WriteLogEntry(BinaryWriter w, LogEntry value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Time);
            w.Write((byte)value.Kind);
            w.Write(value.Actor);
            WriteString(w, value.TargetId);
            w.Write(value.IntArg);
            WriteString(w, value.StrArg);
        }

        private static LogEntry ReadLogEntry(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new LogEntry
            {
                Time = r.ReadDouble(),
                Kind = (EventType)r.ReadByte(),
                Actor = r.ReadInt32(),
                TargetId = ReadString(r),
                IntArg = r.ReadInt32(),
                StrArg = ReadString(r),
            };
        }

        private static void WriteManagerView(BinaryWriter w, ManagerView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write((byte)value.State);
            WriteVec3(w, value.Position);
            w.Write(value.Yaw);
            w.Write(value.InspectingDesk);
            w.Write(value.ReactionObservation);
            w.Write(value.ReactionUntil);
            w.Write(value.ArrivesAt);
            w.Write(value.Surprise);
        }

        private static ManagerView ReadManagerView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new ManagerView
            {
                State = (ManagerState)r.ReadByte(),
                Position = ReadVec3(r),
                Yaw = r.ReadSingle(),
                InspectingDesk = r.ReadInt32(),
                ReactionObservation = r.ReadInt32(),
                ReactionUntil = r.ReadDouble(),
                ArrivesAt = r.ReadDouble(),
                Surprise = ReadBoolean(r),
            };
        }

        private static void WriteMeetingView(BinaryWriter w, MeetingView value)
        {
            w.Write(value != null);
            if (value == null) return;
            WriteList(w, value.Accusations, (writer, item) => WriteAccusationView(writer, item));
            WriteList(w, value.Shortlist, (writer, item) => writer.Write(item));
            w.Write(value.Speaker);
            w.Write(value.SpeakerEnds);
            w.Write(value.BallotsCast);
            WriteList(w, value.Voters, (writer, item) => writer.Write(item));
            WriteList(w, value.PresentedEvidence, (writer, item) => writer.Write(item));
            WriteVerdictView(w, value.Verdict);
        }

        private static MeetingView ReadMeetingView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new MeetingView
            {
                Accusations = ReadList(r, reader => ReadAccusationView(reader)),
                Shortlist = ReadList(r, reader => reader.ReadInt32()),
                Speaker = r.ReadInt32(),
                SpeakerEnds = r.ReadDouble(),
                BallotsCast = r.ReadInt32(),
                Voters = ReadList(r, reader => reader.ReadInt32()),
                PresentedEvidence = ReadList(r, reader => reader.ReadInt32()),
                Verdict = ReadVerdictView(r),
            };
        }

        private static void WriteAccusationView(BinaryWriter w, AccusationView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Accuser);
            w.Write(value.Accused);
            w.Write(value.Time);
        }

        private static AccusationView ReadAccusationView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new AccusationView
            {
                Accuser = r.ReadInt32(),
                Accused = r.ReadInt32(),
                Time = r.ReadDouble(),
            };
        }

        private static void WriteVerdictView(BinaryWriter w, VerdictView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.FiredPlayer);
            WriteList(w, value.Candidates, (writer, item) => WriteCandidateScore(writer, item));
            w.Write((byte)value.TieBreakUsed);
            WriteList(w, value.KeyEvidence, (writer, item) => writer.Write(item));
            WriteList(w, value.KeyObservations, (writer, item) => writer.Write(item));
            w.Write(value.VoteWeight);
            w.Write(value.RiskWeight);
            w.Write(value.PerformanceWeight);
        }

        private static VerdictView ReadVerdictView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new VerdictView
            {
                FiredPlayer = r.ReadInt32(),
                Candidates = ReadList(r, reader => ReadCandidateScore(reader)),
                TieBreakUsed = (TieBreak)r.ReadByte(),
                KeyEvidence = ReadList(r, reader => reader.ReadInt32()),
                KeyObservations = ReadList(r, reader => reader.ReadInt32()),
                VoteWeight = r.ReadSingle(),
                RiskWeight = r.ReadSingle(),
                PerformanceWeight = r.ReadSingle(),
            };
        }

        private static void WriteCandidateScore(BinaryWriter w, CandidateScore value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Player);
            w.Write(value.Accusations);
            w.Write(value.Votes);
            w.Write(value.VoteShare);
            w.Write(value.ObservedRisk);
            w.Write(value.RiskNormalized);
            w.Write(value.Performance);
            w.Write(value.PerformanceDeficit);
            w.Write(value.Favor);
            w.Write(value.FavorDiscount);
            w.Write(value.Score);
        }

        private static CandidateScore ReadCandidateScore(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new CandidateScore
            {
                Player = r.ReadInt32(),
                Accusations = r.ReadInt32(),
                Votes = r.ReadInt32(),
                VoteShare = r.ReadSingle(),
                ObservedRisk = r.ReadSingle(),
                RiskNormalized = r.ReadSingle(),
                Performance = r.ReadSingle(),
                PerformanceDeficit = r.ReadSingle(),
                Favor = r.ReadSingle(),
                FavorDiscount = r.ReadSingle(),
                Score = r.ReadSingle(),
            };
        }

        private static void WritePrivateView(BinaryWriter w, PrivateView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write((byte)value.Role);
            WriteObjectiveView(w, value.Objective);
            WriteInformantView(w, value.Informant);
            WriteList(w, value.Notes, (writer, item) => WritePrivateNote(writer, item));
            WriteInteractionView(w, value.ActiveInteraction);
            w.Write(value.HasAccused);
            w.Write(value.AccusedPlayer);
            w.Write(value.HasVoted);
            w.Write(value.Ballot);
            w.Write(value.RematchVoted);
            WriteList(w, value.SabotageCooldowns, (writer, item) => writer.Write(item));
            WriteString(w, value.ReconnectToken);
            WriteList(w, value.Cards, (writer, item) => writer.Write((byte)item));
            WriteCallView(w, value.Call);
            w.Write(value.CallCooldownUntil);
            w.Write(value.TipsLeft);
            w.Write(value.FootageLeft);
        }

        private static PrivateView ReadPrivateView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new PrivateView
            {
                Role = (Role)r.ReadByte(),
                Objective = ReadObjectiveView(r),
                Informant = ReadInformantView(r),
                Notes = ReadList(r, reader => ReadPrivateNote(reader)),
                ActiveInteraction = ReadInteractionView(r),
                HasAccused = ReadBoolean(r),
                AccusedPlayer = r.ReadInt32(),
                HasVoted = ReadBoolean(r),
                Ballot = r.ReadInt32(),
                RematchVoted = ReadBoolean(r),
                SabotageCooldowns = ReadList(r, reader => reader.ReadDouble()),
                ReconnectToken = ReadString(r),
                Cards = ReadList(r, reader => (SabotageType)reader.ReadByte()),
                Call = ReadCallView(r),
                CallCooldownUntil = r.ReadDouble(),
                TipsLeft = r.ReadInt32(),
                FootageLeft = r.ReadInt32(),
            };
        }

        private static void WriteObjectiveView(BinaryWriter w, ObjectiveView value)
        {
            w.Write(value != null);
            if (value == null) return;
            WriteString(w, value.CardId);
            w.Write((byte)value.Type);
            w.Write(value.Target);
            w.Write(value.Progress);
            w.Write(value.Required);
            w.Write(value.Progress2);
            w.Write(value.Required2);
            w.Write((byte)value.Status);
            WriteList(w, value.Flags, (writer, item) => WriteString(writer, item));
        }

        private static ObjectiveView ReadObjectiveView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new ObjectiveView
            {
                CardId = ReadString(r),
                Type = (ObjectiveType)r.ReadByte(),
                Target = r.ReadInt32(),
                Progress = r.ReadInt32(),
                Required = r.ReadInt32(),
                Progress2 = r.ReadInt32(),
                Required2 = r.ReadInt32(),
                Status = (ObjectiveStatus)r.ReadByte(),
                Flags = ReadList(r, reader => ReadString(reader)),
            };
        }

        private static void WriteInformantView(BinaryWriter w, InformantView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.ScansLeft);
            w.Write(value.SealsLeft);
            w.Write(value.SealedEvidenceId);
            WriteList(w, value.Scans, (writer, item) => WriteScanResultView(writer, item));
            w.Write(value.CallLogsLeft);
        }

        private static InformantView ReadInformantView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new InformantView
            {
                ScansLeft = r.ReadInt32(),
                SealsLeft = r.ReadInt32(),
                SealedEvidenceId = r.ReadInt32(),
                Scans = ReadList(r, reader => ReadScanResultView(reader)),
                CallLogsLeft = r.ReadInt32(),
            };
        }

        private static void WriteScanResultView(BinaryWriter w, ScanResultView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Id);
            w.Write(value.Time);
            w.Write(value.EvidenceId);
            WriteString(w, value.ItemId);
            WriteString(w, value.ItemVariant);
            w.Write(value.DeskIndex);
            w.Write(value.ApparentOwner);
            w.Write((byte)value.Reliability);
            w.Write((byte)value.Signal);
            w.Write(value.NoteId);
        }

        private static ScanResultView ReadScanResultView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new ScanResultView
            {
                Id = r.ReadInt32(),
                Time = r.ReadDouble(),
                EvidenceId = r.ReadInt32(),
                ItemId = ReadString(r),
                ItemVariant = ReadString(r),
                DeskIndex = r.ReadInt32(),
                ApparentOwner = r.ReadInt32(),
                Reliability = (Reliability)r.ReadByte(),
                Signal = (ScanSignal)r.ReadByte(),
                NoteId = r.ReadInt32(),
            };
        }

        private static void WritePrivateNote(BinaryWriter w, PrivateNote value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Id);
            w.Write((byte)value.Kind);
            w.Write(value.Time);
            w.Write(value.Subject);
            w.Write((byte)value.Activity);
            WriteString(w, value.TargetId);
            w.Write(value.DeskIndex);
            WriteString(w, value.HeldBefore);
            WriteString(w, value.HeldAfter);
            WriteList(w, value.Contents, (writer, item) => WriteString(writer, item));
            w.Write(value.ScanId);
            w.Write(value.Presented);
            WriteList(w, value.Calls, (writer, item) => WriteCallRecordView(writer, item));
            WriteString(w, value.ItemId);
            w.Write(value.Salted);
        }

        private static PrivateNote ReadPrivateNote(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new PrivateNote
            {
                Id = r.ReadInt32(),
                Kind = (NoteKind)r.ReadByte(),
                Time = r.ReadDouble(),
                Subject = r.ReadInt32(),
                Activity = (PublicActivity)r.ReadByte(),
                TargetId = ReadString(r),
                DeskIndex = r.ReadInt32(),
                HeldBefore = ReadString(r),
                HeldAfter = ReadString(r),
                Contents = ReadList(r, reader => ReadString(reader)),
                ScanId = r.ReadInt32(),
                Presented = ReadBoolean(r),
                Calls = ReadList(r, reader => ReadCallRecordView(reader)),
                ItemId = ReadString(r),
                Salted = ReadBoolean(r),
            };
        }

        private static void WriteInteractionView(BinaryWriter w, InteractionView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write((byte)value.Verb);
            WriteString(w, value.TargetId);
            w.Write(value.StartTime);
            w.Write(value.EndTime);
        }

        private static InteractionView ReadInteractionView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new InteractionView
            {
                Verb = (ActionVerb)r.ReadByte(),
                TargetId = ReadString(r),
                StartTime = r.ReadDouble(),
                EndTime = r.ReadDouble(),
            };
        }

        private static void WriteMatchResultView(BinaryWriter w, MatchResultView value, bool blankHash = false)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.CompanySucceeded);
            w.Write(value.QuotaPoints);
            w.Write(value.QuotaTarget);
            w.Write(value.FiredPlayer);
            WriteVerdictView(w, value.Verdict);
            w.Write(value.InformantPlayer);
            w.Write(value.InformantWon);
            WriteList(w, value.TopMisconductTier, (writer, item) => writer.Write(item));
            WriteList(w, value.Players, (writer, item) => WritePlayerOutcome(writer, item));
            WriteList(w, value.Incidents, (writer, item) => WriteIncidentReveal(writer, item));
            WriteList(w, value.Recap, (writer, item) => WriteRecapBeat(writer, item));
            w.Write(value.MatchSeed);
            WriteString(w, blankHash ? "" : value.ResultHash);
            w.Write((byte)value.Mode);
            w.Write(value.WeekDay);
            WriteString(w, value.ManagerProfileId);
            WriteList(w, value.Calls, (writer, item) => WriteCallRecordView(writer, item));
            w.Write(value.WeekFinished);
            WriteList(w, value.EmployeeOfTheMonth, (writer, item) => writer.Write(item));
            WriteList(w, value.Favourites, (writer, item) => writer.Write(item));
            WriteHighlightView(w, value.Highlight);
        }

        private static MatchResultView ReadMatchResultView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new MatchResultView
            {
                CompanySucceeded = ReadBoolean(r),
                QuotaPoints = r.ReadSingle(),
                QuotaTarget = r.ReadSingle(),
                FiredPlayer = r.ReadInt32(),
                Verdict = ReadVerdictView(r),
                InformantPlayer = r.ReadInt32(),
                InformantWon = ReadBoolean(r),
                TopMisconductTier = ReadList(r, reader => reader.ReadInt32()),
                Players = ReadList(r, reader => ReadPlayerOutcome(reader)),
                Incidents = ReadList(r, reader => ReadIncidentReveal(reader)),
                Recap = ReadList(r, reader => ReadRecapBeat(reader)),
                MatchSeed = r.ReadInt32(),
                ResultHash = ReadString(r),
                Mode = (GameMode)r.ReadByte(),
                WeekDay = r.ReadInt32(),
                ManagerProfileId = ReadString(r),
                Calls = ReadList(r, reader => ReadCallRecordView(reader)),
                WeekFinished = ReadBoolean(r),
                EmployeeOfTheMonth = ReadList(r, reader => reader.ReadInt32()),
                Favourites = ReadList(r, reader => reader.ReadInt32()),
                Highlight = ReadHighlightView(r),
            };
        }

        private static void WritePlayerOutcome(BinaryWriter w, PlayerOutcome value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Player);
            WriteString(w, value.Name);
            w.Write((byte)value.Role);
            WriteString(w, value.ObjectiveCardId);
            w.Write((byte)value.ObjectiveType);
            w.Write(value.ObjectiveTarget);
            w.Write(value.ObjectiveCompleted);
            w.Write(value.Fired);
            w.Write(value.Won);
            WriteString(w, value.ExplanationKey);
            w.Write(value.TrueMisconduct);
            w.Write(value.MisconductRank);
            w.Write(value.FinalPerformance);
            w.Write(value.FalseAccusationPenalty);
            w.Write(value.TasksCompleted);
            w.Write(value.SabotageCount);
            WriteList(w, value.Titles, (writer, item) => WriteString(writer, item));
            WriteList(w, value.Cards, (writer, item) => writer.Write((byte)item));
            w.Write(value.Favor);
            w.Write(value.CareerDelta);
            w.Write(value.CareerTotal);
        }

        private static PlayerOutcome ReadPlayerOutcome(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new PlayerOutcome
            {
                Player = r.ReadInt32(),
                Name = ReadString(r),
                Role = (Role)r.ReadByte(),
                ObjectiveCardId = ReadString(r),
                ObjectiveType = (ObjectiveType)r.ReadByte(),
                ObjectiveTarget = r.ReadInt32(),
                ObjectiveCompleted = ReadBoolean(r),
                Fired = ReadBoolean(r),
                Won = ReadBoolean(r),
                ExplanationKey = ReadString(r),
                TrueMisconduct = r.ReadSingle(),
                MisconductRank = r.ReadInt32(),
                FinalPerformance = r.ReadSingle(),
                FalseAccusationPenalty = r.ReadSingle(),
                TasksCompleted = r.ReadInt32(),
                SabotageCount = r.ReadInt32(),
                Titles = ReadList(r, reader => ReadString(reader)),
                Cards = ReadList(r, reader => (SabotageType)reader.ReadByte()),
                Favor = r.ReadSingle(),
                CareerDelta = r.ReadInt32(),
                CareerTotal = r.ReadInt32(),
            };
        }

        private static void WriteIncidentReveal(BinaryWriter w, IncidentReveal value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Id);
            w.Write((byte)value.Type);
            w.Write(value.Actor);
            w.Write(value.Victim);
            w.Write(value.DeskIndex);
            WriteString(w, value.ItemId);
            w.Write(value.Time);
            w.Write(value.Misconduct);
            w.Write(value.Discovered);
            w.Write(value.ReversedBy);
            w.Write(value.BlamedPlayer);
        }

        private static IncidentReveal ReadIncidentReveal(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new IncidentReveal
            {
                Id = r.ReadInt32(),
                Type = (SabotageType)r.ReadByte(),
                Actor = r.ReadInt32(),
                Victim = r.ReadInt32(),
                DeskIndex = r.ReadInt32(),
                ItemId = ReadString(r),
                Time = r.ReadDouble(),
                Misconduct = r.ReadSingle(),
                Discovered = ReadBoolean(r),
                ReversedBy = r.ReadInt32(),
                BlamedPlayer = r.ReadInt32(),
            };
        }

        private static void WriteRecapBeat(BinaryWriter w, RecapBeat value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Time);
            WriteString(w, value.Key);
            w.Write(value.Actor);
            w.Write(value.Other);
            WriteString(w, value.ItemVariant);
            w.Write(value.DeskIndex);
        }

        private static RecapBeat ReadRecapBeat(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new RecapBeat
            {
                Time = r.ReadDouble(),
                Key = ReadString(r),
                Actor = r.ReadInt32(),
                Other = r.ReadInt32(),
                ItemVariant = ReadString(r),
                DeskIndex = r.ReadInt32(),
            };
        }

        private static void WriteActionRequest(BinaryWriter w, ActionRequest value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.MatchId);
            w.Write(value.RequestId);
            w.Write((byte)value.Verb);
            WriteString(w, value.TargetId);
            w.Write(value.IntParam);
        }

        private static ActionRequest ReadActionRequest(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new ActionRequest
            {
                MatchId = r.ReadInt32(),
                RequestId = r.ReadUInt32(),
                Verb = (ActionVerb)r.ReadByte(),
                TargetId = ReadString(r),
                IntParam = r.ReadInt32(),
            };
        }

        private static void WriteActionResult(BinaryWriter w, ActionResult value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.RequestId);
            w.Write(value.Accepted);
            w.Write((byte)value.Failure);
            w.Write(value.CompletesAt);
        }

        private static ActionResult ReadActionResult(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new ActionResult
            {
                RequestId = r.ReadUInt32(),
                Accepted = ReadBoolean(r),
                Failure = (FailureCode)r.ReadByte(),
                CompletesAt = r.ReadDouble(),
            };
        }

        private static void WriteGameEvent(BinaryWriter w, GameEvent value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write((byte)value.Type);
            w.Write(value.Time);
            w.Write((byte)value.Audience);
            w.Write(value.AudiencePlayer);
            w.Write(value.Actor);
            WriteString(w, value.TargetId);
            WriteString(w, value.ItemId);
            w.Write(value.IntArg);
            WriteString(w, value.StrArg);
            w.Write((byte)value.Phase);
        }

        private static GameEvent ReadGameEvent(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new GameEvent
            {
                Type = (EventType)r.ReadByte(),
                Time = r.ReadDouble(),
                Audience = (Audience)r.ReadByte(),
                AudiencePlayer = r.ReadInt32(),
                Actor = r.ReadInt32(),
                TargetId = ReadString(r),
                ItemId = ReadString(r),
                IntArg = r.ReadInt32(),
                StrArg = ReadString(r),
                Phase = (MatchPhase)r.ReadByte(),
            };
        }

        private static void WriteLobbyView(BinaryWriter w, LobbyView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.ProtocolVersion);
            WriteString(w, value.BuildId);
            WriteList(w, value.Slots, (writer, item) => WriteLobbySlot(writer, item));
            w.Write(value.MatchRunning);
            w.Write(value.MatchId);
            WriteString(w, value.StatusKey);
            w.Write((byte)value.Mode);
            w.Write(value.WeekDay);
            WriteList(w, value.CareerPoints, (writer, item) => writer.Write(item));
        }

        private static LobbyView ReadLobbyView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new LobbyView
            {
                ProtocolVersion = r.ReadInt32(),
                BuildId = ReadString(r),
                Slots = ReadList(r, reader => ReadLobbySlot(reader)),
                MatchRunning = ReadBoolean(r),
                MatchId = r.ReadInt32(),
                StatusKey = ReadString(r),
                Mode = (GameMode)r.ReadByte(),
                WeekDay = r.ReadInt32(),
                CareerPoints = ReadList(r, reader => reader.ReadInt32()),
            };
        }

        private static void WriteLobbySlot(BinaryWriter w, LobbySlot value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Slot);
            w.Write(value.Occupied);
            w.Write(value.Connected);
            w.Write(value.Ready);
            w.Write(value.IsHost);
            WriteString(w, value.Name);
            w.Write(value.IsBot);
        }

        private static LobbySlot ReadLobbySlot(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new LobbySlot
            {
                Slot = r.ReadInt32(),
                Occupied = ReadBoolean(r),
                Connected = ReadBoolean(r),
                Ready = ReadBoolean(r),
                IsHost = ReadBoolean(r),
                Name = ReadString(r),
                IsBot = ReadBoolean(r),
            };
        }

        private static void WriteConnectionPayload(BinaryWriter w, ConnectionPayload value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.ProtocolVersion);
            WriteString(w, value.BuildId);
            WriteString(w, value.PlayerName);
            WriteString(w, value.ReconnectToken);
        }

        private static ConnectionPayload ReadConnectionPayload(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new ConnectionPayload
            {
                ProtocolVersion = r.ReadInt32(),
                BuildId = ReadString(r),
                PlayerName = ReadString(r),
                ReconnectToken = ReadString(r),
            };
        }

        private static void WriteWelcomeMessage(BinaryWriter w, WelcomeMessage value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Slot);
            WriteString(w, value.ReconnectToken);
            w.Write(value.Reconnected);
            w.Write(value.MatchId);
        }

        private static WelcomeMessage ReadWelcomeMessage(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new WelcomeMessage
            {
                Slot = r.ReadInt32(),
                ReconnectToken = ReadString(r),
                Reconnected = ReadBoolean(r),
                MatchId = r.ReadInt32(),
            };
        }

        private static void WriteFeaturesView(BinaryWriter w, FeaturesView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Cards);
            w.Write(value.Calls);
            w.Write(value.TipLine);
            w.Write(value.Footage);
            w.Write(value.Mail);
            w.Write(value.CareerMultiplier);
        }

        private static FeaturesView ReadFeaturesView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new FeaturesView
            {
                Cards = ReadBoolean(r),
                Calls = ReadBoolean(r),
                TipLine = ReadBoolean(r),
                Footage = ReadBoolean(r),
                Mail = ReadBoolean(r),
                CareerMultiplier = r.ReadSingle(),
            };
        }

        private static void WriteOfficeStatusView(BinaryWriter w, OfficeStatusView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.InternetDown);
            w.Write(value.InternetBackAt);
            w.Write(value.PowerDown);
            w.Write(value.PowerBackAt);
        }

        private static OfficeStatusView ReadOfficeStatusView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new OfficeStatusView
            {
                InternetDown = ReadBoolean(r),
                InternetBackAt = r.ReadDouble(),
                PowerDown = ReadBoolean(r),
                PowerBackAt = r.ReadDouble(),
            };
        }

        private static void WriteEmailView(BinaryWriter w, EmailView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Id);
            w.Write(value.Time);
            w.Write(value.FromPlayer);
            w.Write(value.DeskIndex);
            w.Write(value.TemplateId);
        }

        private static EmailView ReadEmailView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new EmailView
            {
                Id = r.ReadInt32(),
                Time = r.ReadDouble(),
                FromPlayer = r.ReadInt32(),
                DeskIndex = r.ReadInt32(),
                TemplateId = r.ReadInt32(),
            };
        }

        private static void WriteCallView(BinaryWriter w, CallView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write((byte)value.State);
            w.Write(value.Other);
            w.Write(value.Since);
            w.Write(value.Until);
        }

        private static CallView ReadCallView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new CallView
            {
                State = (CallState)r.ReadByte(),
                Other = r.ReadInt32(),
                Since = r.ReadDouble(),
                Until = r.ReadDouble(),
            };
        }

        private static void WriteFootageSighting(BinaryWriter w, FootageSighting value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Player);
            w.Write(value.Time);
            w.Write((byte)value.Activity);
            WriteString(w, value.Carrying);
            w.Write(value.Crouched);
            WriteVec3(w, value.Position);
            w.Write(value.Yaw);
        }

        private static FootageSighting ReadFootageSighting(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new FootageSighting
            {
                Player = r.ReadInt32(),
                Time = r.ReadDouble(),
                Activity = (PublicActivity)r.ReadByte(),
                Carrying = ReadString(r),
                Crouched = ReadBoolean(r),
                Position = ReadVec3(r),
                Yaw = r.ReadSingle(),
            };
        }

        private static void WriteHighlightView(BinaryWriter w, HighlightView value)
        {
            w.Write(value != null);
            if (value == null) return;
            WriteString(w, value.Key);
            w.Write(value.IncidentId);
            w.Write(value.Actor);
            w.Write(value.DeskIndex);
            w.Write(value.Start);
            w.Write(value.End);
            WriteList(w, value.Samples, (writer, item) => WriteFootageSighting(writer, item));
        }

        private static HighlightView ReadHighlightView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new HighlightView
            {
                Key = ReadString(r),
                IncidentId = r.ReadInt32(),
                Actor = r.ReadInt32(),
                DeskIndex = r.ReadInt32(),
                Start = r.ReadDouble(),
                End = r.ReadDouble(),
                Samples = ReadList(r, reader => ReadFootageSighting(reader)),
            };
        }

        private static void WriteCallRecordView(BinaryWriter w, CallRecordView value)
        {
            w.Write(value != null);
            if (value == null) return;
            w.Write(value.Caller);
            w.Write(value.Callee);
            w.Write(value.Start);
            w.Write(value.End);
            w.Write(value.Answered);
            w.Write(value.Tip);
            w.Write(value.TipDesk);
        }

        private static CallRecordView ReadCallRecordView(BinaryReader r)
        {
            if (!ReadBoolean(r)) return null;
            return new CallRecordView
            {
                Caller = r.ReadInt32(),
                Callee = r.ReadInt32(),
                Start = r.ReadDouble(),
                End = r.ReadDouble(),
                Answered = ReadBoolean(r),
                Tip = ReadBoolean(r),
                TipDesk = r.ReadInt32(),
            };
        }

    }
}
