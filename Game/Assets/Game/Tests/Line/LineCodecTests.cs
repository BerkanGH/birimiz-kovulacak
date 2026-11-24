using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class LineCodecTests
    {
        private const BindingFlags SchemaMethods = BindingFlags.NonPublic | BindingFlags.Static;

        public static IEnumerable<Type> ContractTypes()
        {
            var types = new List<Type>();
            foreach (Type type in typeof(LineView).Assembly.GetTypes())
                if (type.Namespace == typeof(LineView).Namespace && type.Name.StartsWith("Line", StringComparison.Ordinal)
                    && type.IsClass && type.IsDefined(typeof(SerializableAttribute), false) && !type.IsAbstract)
                    types.Add(type);
            types.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return types;
        }

        private static object Populated(Type type, ref int serial, object previous = null)
        {
            serial++;
            if (type == typeof(bool)) return previous == null || !(bool)previous;
            if (type == typeof(int)) return -123456 - serial;
            if (type == typeof(uint)) return (uint)(3000000000U + serial);
            if (type == typeof(long)) return -1234567890123L - serial;
            if (type == typeof(ulong)) return 12345678901234567890UL + (ulong)serial;
            if (type == typeof(float)) return 17.125f + serial;
            if (type == typeof(double)) return 987654.125 + serial;
            if (type == typeof(string)) return "İhtar/Şifre/你好/🎧/" + serial;
            if (type.IsEnum)
            {
                Array values = Enum.GetValues(type);
                for (int i = values.Length - 1; i >= 0; i--)
                    if (!Equals(previous, values.GetValue(i))) return values.GetValue(i);
                Assert.Fail("Enum requires two values for non-default fixture: " + type);
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)Activator.CreateInstance(type);
                list.Add(Populated(type.GetGenericArguments()[0], ref serial));
                list.Add(Populated(type.GetGenericArguments()[0], ref serial));
                return list;
            }
            object value = Activator.CreateInstance(type);
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                field.SetValue(value, Populated(field.FieldType, ref serial, field.GetValue(value)));
            return value;
        }

        private static void EqualGraph(object expected, object actual, string path)
        {
            if (expected == null || actual == null) { Assert.AreEqual(expected, actual, path); return; }
            Type type = expected.GetType();
            Assert.AreEqual(type, actual.GetType(), path);
            if (type.IsPrimitive || type.IsEnum || type == typeof(string)) { Assert.AreEqual(expected, actual, path); return; }
            if (expected is IList list)
            {
                var other = (IList)actual;
                Assert.AreEqual(list.Count, other.Count, path);
                for (int i = 0; i < list.Count; i++) EqualGraph(list[i], other[i], path + "[" + i + "]");
                return;
            }
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                EqualGraph(field.GetValue(expected), field.GetValue(actual), path + "." + field.Name);
        }

        private static object SchemaRoundTrip(Type type, object value)
        {
            MethodInfo write = typeof(LineCodec).GetMethod("Write" + type.Name, SchemaMethods);
            MethodInfo read = typeof(LineCodec).GetMethod("Read" + type.Name, SchemaMethods);
            Assert.NotNull(write, "Missing explicit writer for new contract type " + type.Name);
            Assert.NotNull(read, "Missing explicit reader for new contract type " + type.Name);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                write.Invoke(null, new[] { writer, value });
                writer.Flush(); stream.Position = 0;
                object decoded = read.Invoke(null, new object[] { reader });
                Assert.AreEqual(stream.Length, stream.Position, "Schema must consume every byte: " + type.Name);
                return decoded;
            }
        }

        [TestCaseSource(nameof(ContractTypes))]
        public void EveryContractFieldRoundTripsWithNonDefaultValues(Type type)
        {
            int serial = 0;
            object original = Populated(type, ref serial);
            EqualGraph(original, SchemaRoundTrip(type, original), type.Name);
        }

        [TestCaseSource(nameof(ContractTypes))]
        public void EveryContractSupportsNullAndEmptyReferences(Type type)
        {
            Assert.IsNull(SchemaRoundTrip(type, null));
            object original = Activator.CreateInstance(type);
            EqualGraph(original, SchemaRoundTrip(type, original), type.Name + " defaults");
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (!field.FieldType.IsValueType) field.SetValue(original, null);
            EqualGraph(original, SchemaRoundTrip(type, original), type.Name + " null references");
        }

        private sealed class Message
        {
            internal Type Type;
            internal Func<object, byte[]> Encode;
            internal Func<byte[], object> Decode;
        }

        private static Message MessageFor(int kind)
        {
            switch (kind)
            {
                case 1: return new Message { Type = typeof(LineView), Encode = v => LineCodec.EncodeView((LineView)v), Decode = b => LineCodec.DecodeView(b) };
                case 2: return new Message { Type = typeof(LineAction), Encode = v => LineCodec.EncodeAction((LineAction)v), Decode = b => LineCodec.DecodeAction(b) };
                case 3: return new Message { Type = typeof(LineActionResult), Encode = v => LineCodec.EncodeResult((LineActionResult)v), Decode = b => LineCodec.DecodeResult(b) };
                case 4: return new Message { Type = typeof(List<LineEvent>), Encode = v => LineCodec.EncodeEvents((List<LineEvent>)v), Decode = b => LineCodec.DecodeEvents(b) };
                case 5: return new Message { Type = typeof(LineLobbyView), Encode = v => LineCodec.EncodeLobby((LineLobbyView)v), Decode = b => LineCodec.DecodeLobby(b) };
                case 6: return new Message { Type = typeof(LineWelcome), Encode = v => LineCodec.EncodeWelcome((LineWelcome)v), Decode = b => LineCodec.DecodeWelcome(b) };
                case 12: return new Message { Type = typeof(List<LineMessage>), Encode = v => LineCodec.EncodeMessages((List<LineMessage>)v), Decode = b => LineCodec.DecodeMessages(b) };
                case 7: return new Message { Type = typeof(LineConnectionPayload), Encode = v => LineCodec.EncodeConnection((LineConnectionPayload)v), Decode = b => LineCodec.DecodeConnection(b) };
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        [TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5), TestCase(6), TestCase(7), TestCase(12)]
        public void PublicMessagesAreVersionedDeterministicAndRoundTrip(int kind)
        {
            Message m = MessageFor(kind); int serial = 0;
            object original = Populated(m.Type, ref serial);
            byte[] bytes = m.Encode(original);
            Assert.AreEqual(0xB3, bytes[0]);
            Assert.AreEqual(LineProtocol.Version, bytes[1] | bytes[2] << 8);
            Assert.AreEqual(kind, bytes[3]);
            EqualGraph(original, m.Decode(bytes), m.Type.Name);
            CollectionAssert.AreEqual(bytes, m.Encode(m.Decode(bytes)));
            Assert.IsNull(m.Decode(m.Encode(null)));
        }

        [TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5), TestCase(6), TestCase(7), TestCase(12)]
        public void GarbageTruncationForeignMessagesAndTrailingDataThrowFormatException(int kind)
        {
            Message m = MessageFor(kind); int serial = 0;
            byte[] valid = m.Encode(Populated(m.Type, ref serial));
            Assert.Throws<FormatException>(() => m.Decode(null));
            for (int length = 0; length < valid.Length; length++)
            {
                byte[] truncated = new byte[length]; Array.Copy(valid, truncated, length);
                Assert.Throws<FormatException>(() => m.Decode(truncated), "length " + length);
            }
            for (int index = 0; index < 4; index++)
            {
                byte[] foreign = (byte[])valid.Clone(); foreign[index] ^= 0x7F;
                Assert.Throws<FormatException>(() => m.Decode(foreign), "header " + index);
            }
            byte[] trailing = new byte[valid.Length + 1]; Array.Copy(valid, trailing, valid.Length);
            Assert.Throws<FormatException>(() => m.Decode(trailing));
            uint state=(uint)kind;
            for (int i = 0; i < 100; i++)
            {
                state=unchecked(state*1664525+1013904223);byte[] garbage=new byte[state%512];for(int j=0;j<garbage.Length;j++){state=unchecked(state*1664525+1013904223);garbage[j]=(byte)state;}
                if (garbage.Length > 0) garbage[0] = 0;
                Assert.Throws<FormatException>(() => m.Decode(garbage));
            }
        }

        [Test]
        public void InvalidBooleanEnumLengthAndUtf8AreRejected()
        {
            byte[] action = LineCodec.EncodeAction(new LineAction());
            action[4] = 2;
            Assert.Throws<FormatException>(() => LineCodec.DecodeAction(action));
            action[4] = 1; action[13] = 255;
            Assert.Throws<FormatException>(() => LineCodec.DecodeAction(action));
            byte[] malformedLength = { 0xB3, 5, 0, 4, 255, 255, 255, 255, 255 };
            Assert.Throws<FormatException>(() => LineCodec.DecodeEvents(malformedLength));
            byte[] overlongLength = { 0xB3, 5, 0, 4, 128, 0 };
            Assert.Throws<FormatException>(() => LineCodec.DecodeEvents(overlongLength));
            byte[] welcome = LineCodec.EncodeWelcome(new LineWelcome { ReconnectToken = "a" });
            welcome[10] = 0xFF;
            Assert.Throws<FormatException>(() => LineCodec.DecodeWelcome(welcome));
        }

        [Test]
        public void OversizedPayloadAndListsAreRejectedBeforeAllocation()
        {
            Assert.Throws<FormatException>(() => LineCodec.DecodeView(new byte[1024 * 1024 + 1]));
            var events = new List<LineEvent>();
            for (int i = 0; i < 4097; i++) events.Add(null);
            Assert.Throws<FormatException>(() => LineCodec.EncodeEvents(events));
            byte[] declaredHugeList = { 0xB3, 5, 0, 4, 255, 255, 127 };
            Assert.Throws<FormatException>(() => LineCodec.DecodeEvents(declaredHugeList));
        }

        [Test]
        public void NumericWireOrderIsLittleEndianAndEnumsAreOneByte()
        {
            byte[] bytes = LineCodec.EncodeAction(new LineAction(0x01020304, 0xA1B2C3D4, LineVerb.SubmitTask, -1, 0x11223344, 7));
            CollectionAssert.AreEqual(new byte[] {0xB3,8,0,2,1,4,3,2,1,0xD4,0xC3,0xB2,0xA1,5,255,255,255,255,0x44,0x33,0x22,0x11,7,0,0,0,255,255,255,255,1},bytes);
        }

        [Test]
        public void StringsAndEnumGapsAreBounded()
        {
            Assert.Throws<FormatException>(()=>LineCodec.EncodeAction(new LineAction{Text=new string('a',2049)}));
        }

        [Test]
        public void HashIgnoresHashFieldButCoversOutcomeAndDoesNotMutate()
        {
            var method = typeof(LineCodec).GetMethod("HashResult", SchemaMethods);
            var result = new LineWeekResult { WeekId = 17, Fired = new List<int> { 2 }, ResultHash = 1234 };
            ulong first = (ulong)method.Invoke(null, new object[] { result });
            Assert.AreEqual(1234UL, result.ResultHash);
            result.MatchId = "unrelated-host-id";
            result.ResultHash = 5678;
            Assert.AreEqual(first, (ulong)method.Invoke(null, new object[] { result }));
            result.Fired[0] = 1;
            Assert.AreNotEqual(first, (ulong)method.Invoke(null, new object[] { result }));
        }

        [Test]
        public void HashKeepsItsFrozenSchemaAndCoversDeclarationsOnlyWhenThereAreSome()
        {
            var method = typeof(LineCodec).GetMethod("HashResult", SchemaMethods);
            Func<LineWeekResult, ulong> hash = x => (ulong)method.Invoke(null, new object[] { x });
            var result = new LineWeekResult { WeekId = 17, Fired = new List<int> { 2 } };
            result.Firings.Add(new LineFiringView { Player = 2, Desk = 9 });
            ulong plain = hash(result);
            // v5.2 fields that follow from what is already hashed do not move the hash of an outcome that did not change
            result.Groups.Add(new LineGroupView { TaskNumber = 0, Task = TaskKind.ClientBonus, Members = new List<int> { 0, 1 } });
            result.Firings[0].Time = 321.5;
            Assert.AreEqual(plain, hash(result));
            // the declarations are part of the outcome
            result.Declarations.Add(new LineDeclarationView { Player = 2, Amount = 7, Actual = 9 });
            ulong declared = hash(result);
            Assert.AreNotEqual(plain, declared);
            result.Declarations[0].Amount = 8;
            Assert.AreNotEqual(declared, hash(result));
            result.Declarations[0].Amount = 7;
            result.Declarations[0].Actual = 10;
            Assert.AreNotEqual(declared, hash(result));
            // and the full wire format carries every new field
            result.Declarations[0].Actual = 9;
            result.Firings[0].Declared = 7;
            var view = new LineView { Results = result };
            var back = LineCodec.DecodeView(LineCodec.EncodeView(view)).Results;
            Assert.AreEqual(7, back.Firings[0].Declared);
            Assert.AreEqual(321.5, back.Firings[0].Time);
            Assert.AreEqual(9, back.Declarations[0].Actual);
            CollectionAssert.AreEqual(new[] { 0, 1 }, back.Groups[0].Members);
        }

        [Test]
        public void TheDeclareVerbAndItsEventTravel()
        {
            var action = LineCodec.DecodeAction(LineCodec.EncodeAction(new LineAction(3, 9, LineVerb.Declare, -1, 42)));
            Assert.AreEqual(LineVerb.Declare, action.Verb);
            Assert.AreEqual(42, action.A);
            var events = LineCodec.DecodeEvents(LineCodec.EncodeEvents(new List<LineEvent> { new LineEvent { Type = LineEventType.DeclarationsRevealed, A = 2, B = 3 } }));
            Assert.AreEqual(LineEventType.DeclarationsRevealed, events[0].Type);
            var lobby = LineCodec.DecodeLobby(LineCodec.EncodeLobby(new LineLobbyView { TalkTime = LineTalkTime.Long }));
            Assert.AreEqual(LineTalkTime.Long, lobby.TalkTime);
        }
    }
}
namespace BirimizKovulacak.Core.Tests
{
    [NUnit.Framework.TestFixture]
    public sealed class LineCodecBoundaryTests
    {
        [NUnit.Framework.Test]
        public void GapsInEventEnumAreRejectedAndMaximumLegalLengthsRoundTrip()
        {
            var bytes=LineCodec.EncodeEvents(new System.Collections.Generic.List<LineEvent>{new LineEvent{Type=LineEventType.PhaseChanged}});bytes[6]=33;NUnit.Framework.Assert.Throws<System.FormatException>(()=>LineCodec.DecodeEvents(bytes));
            var action=new LineAction{Text=new string('a',2048)};NUnit.Framework.Assert.AreEqual(action.Text,LineCodec.DecodeAction(LineCodec.EncodeAction(action)).Text);
            var messages=new System.Collections.Generic.List<LineMessage>();for(int i=0;i<4096;i++)messages.Add(null);NUnit.Framework.Assert.AreEqual(4096,LineCodec.DecodeMessages(LineCodec.EncodeMessages(messages)).Count);messages.Add(null);NUnit.Framework.Assert.Throws<System.FormatException>(()=>LineCodec.EncodeMessages(messages));
        }
    }
}
