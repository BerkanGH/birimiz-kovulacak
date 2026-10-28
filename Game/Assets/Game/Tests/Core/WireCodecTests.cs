using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace BirimizKovulacak.Core.Tests
{
    [TestFixture]
    public sealed class WireCodecTests
    {
        [TestCase("view")]
        [TestCase("request")]
        [TestCase("result")]
        [TestCase("events")]
        [TestCase("lobby")]
        [TestCase("connection")]
        [TestCase("welcome")]
        public void FullyPopulatedMessagesRoundTripEveryContractField(string message)
        {
            int serial = 1;
            object original = Populate(MessageType(message), ref serial);
            byte[] bytes = Encode(message, original);
            object decoded = Decode(message, bytes);
            AssertFields(original, decoded, message);
            CollectionAssert.AreEqual(bytes, Encode(message, decoded), "Encoding must be canonical.");
        }

        [Test]
        public void NullAndEmptyFieldsAreDistinctAndRoundTrip()
        {
            var view = new PlayerView
            {
                Items = null,
                Players = new List<PlayerPublicView> { null, new PlayerPublicView { Name = null, CarryingItemId = "" } },
                Me = new PrivateView { Notes = null, Informant = null, Objective = null },
                Result = null
            };
            AssertFields(view, WireCodec.DecodeView(WireCodec.EncodeView(view)), "view");
            Assert.IsNull(WireCodec.DecodeView(WireCodec.EncodeView(null)));
            Assert.IsNull(WireCodec.DecodeEvents(WireCodec.EncodeEvents(null)));
            Assert.AreEqual(0, WireCodec.DecodeEvents(WireCodec.EncodeEvents(new List<GameEvent>())).Count);
        }

        [TestCase("view")]
        [TestCase("request")]
        [TestCase("result")]
        [TestCase("events")]
        [TestCase("lobby")]
        [TestCase("connection")]
        [TestCase("welcome")]
        public void MalformedHeadersTruncationAndTrailingBytesThrowFormatException(string message)
        {
            int serial = 3;
            byte[] bytes = Encode(message, Populate(MessageType(message), ref serial));
            int[] lengths = { 0, 1, 2, 3, 4, bytes.Length / 2, bytes.Length - 1 };
            foreach (int length in lengths)
            {
                var prefix = new byte[length];
                Array.Copy(bytes, prefix, length);
                Assert.Throws<FormatException>(() => Decode(message, prefix), "Truncated length " + length);
            }
            var wrongTag = (byte[])bytes.Clone();
            wrongTag[0] = 255;
            Assert.Throws<FormatException>(() => Decode(message, wrongTag));
            var wrongVersion = (byte[])bytes.Clone();
            wrongVersion[1] = 255;
            Assert.Throws<FormatException>(() => Decode(message, wrongVersion));
            var trailing = new byte[bytes.Length + 1];
            Array.Copy(bytes, trailing, bytes.Length);
            Assert.Throws<FormatException>(() => Decode(message, trailing));
            Assert.Throws<FormatException>(() => Decode(message, null));
        }

        [Test]
        public void RequestHasDocumentedLittleEndianHeaderUtf8AndCompactLength()
        {
            var request = new ActionRequest(0x01020304, 0x11223344, ActionVerb.Place, "é", -1);
            var expected = new byte[]
            {
                2, 2, 0, 1, 4, 3, 2, 1, 0x44, 0x33, 0x22, 0x11, 2, 3, 0xc3, 0xa9, 255, 255, 255, 255
            };
            CollectionAssert.AreEqual(expected, WireCodec.EncodeRequest(request));
            for (int length = 0; length < expected.Length; length++)
            {
                var prefix = new byte[length];
                Array.Copy(expected, prefix, length);
                Assert.Throws<FormatException>(() => WireCodec.DecodeRequest(prefix));
            }
        }

        [Test]
        public void InvalidBooleanUtf8AndHostileLengthsThrowFormatException()
        {
            var bytes = WireCodec.EncodeRequest(new ActionRequest(1, 2, ActionVerb.Place, "a"));
            var invalidBoolean = (byte[])bytes.Clone();
            invalidBoolean[3] = 2;
            Assert.Throws<FormatException>(() => WireCodec.DecodeRequest(invalidBoolean));
            var invalidUtf8 = (byte[])bytes.Clone();
            invalidUtf8[14] = 0xff;
            Assert.Throws<FormatException>(() => WireCodec.DecodeRequest(invalidUtf8));

            foreach (byte[] length in new[]
            {
                new byte[] { 255, 255, 255, 255, 127 },
                new byte[] { 128, 0 },
                new byte[] { 255, 127 },
                new byte[] { 128, 128, 128, 128, 128, 0 }
            })
            {
                var malformed = new byte[13 + length.Length];
                Array.Copy(bytes, malformed, 13);
                Array.Copy(length, 0, malformed, 13, length.Length);
                Assert.Throws<FormatException>(() => WireCodec.DecodeRequest(malformed));
            }
        }

        [Test]
        public void ResultHashIgnoresItsOwnFieldDoesNotMutateAndDetectsContentChanges()
        {
            int serial = 8;
            var result = (MatchResultView)Populate(typeof(MatchResultView), ref serial);
            result.ResultHash = "original";
            string first = WireCodec.HashResult(result);
            Assert.AreEqual("original", result.ResultHash);
            result.ResultHash = "different";
            Assert.AreEqual(first, WireCodec.HashResult(result));
            Assert.AreEqual(16, first.Length);
            Assert.That(first, Does.Match("^[0-9a-f]{16}$"));
            result.Incidents[0].Actor++;
            Assert.AreNotEqual(first, WireCodec.HashResult(result));
        }

        [Test]
        public void ResultHashCoversEveryResultFieldIncludingNestedV2Fields()
        {
            int serial = 1;
            var result = (MatchResultView)Populate(typeof(MatchResultView), ref serial);
            string hash = WireCodec.HashResult(result);
            int mutations = 0;
            VisitLeaves(result, "Result", (type, original, assign, path) =>
            {
                if (path == "Result.ResultHash") return;
                assign(DifferentValue(type, original));
                Assert.AreNotEqual(hash, WireCodec.HashResult(result), path);
                assign(original);
                mutations++;
            });
            Assert.That(mutations, Is.GreaterThan(100));
            Assert.AreEqual(hash, WireCodec.HashResult(result));
        }

        [TestCase(typeof(MatchConfig))]
        [TestCase(typeof(OfficeLayout))]
        [TestCase(typeof(MatchSetup))]
        [TestCase(typeof(SessionHistory))]
        [TestCase(typeof(PlayerView))]
        [TestCase(typeof(HostDebugView))]
        [TestCase(typeof(HighlightView))]
        public void SnapshotsCopyEveryContractFieldWithoutSharingMutableObjects(Type type)
        {
            int serial = 1;
            object original = Populate(type, ref serial);
            Type snapshots = typeof(MatchEngine).Assembly.GetType("BirimizKovulacak.Core.Snapshot", true);
            MethodInfo copy = snapshots.GetMethod("Copy", BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { type }, null);
            Assert.IsNotNull(copy, type.Name);
            object snapshot = copy.Invoke(null, new[] { original });
            AssertFields(original, snapshot, type.Name);
            AssertDetached(original, snapshot, type.Name);
            Assert.IsNull(copy.Invoke(null, new object[] { null }));
        }

        [Test]
        public void V2EnumsAndNestedNullListsRoundTrip()
        {
            foreach (ActionVerb verb in Enum.GetValues(typeof(ActionVerb)))
                Assert.AreEqual(verb, WireCodec.DecodeRequest(WireCodec.EncodeRequest(new ActionRequest(1, 1, verb))).Verb);
            foreach (FailureCode code in Enum.GetValues(typeof(FailureCode)))
                Assert.AreEqual(code, WireCodec.DecodeResult(WireCodec.EncodeResult(ActionResult.Fail(1, code))).Failure);
            foreach (EventType type in Enum.GetValues(typeof(EventType)))
                Assert.AreEqual(type, WireCodec.DecodeEvents(WireCodec.EncodeEvents(new List<GameEvent> { new GameEvent { Type = type } }))[0].Type);
            var view = new PlayerView
            {
                Features = null,
                Office = null,
                Inbox = new List<EmailView> { null },
                Evidence = new List<EvidenceView> { new EvidenceView { Contents = null, Calls = null, Sightings = new List<FootageSighting> { null } } },
                Me = new PrivateView { Cards = null, Call = null, Notes = new List<PrivateNote> { new PrivateNote { Calls = null, ItemId = null } } },
                Result = new MatchResultView { Calls = new List<CallRecordView> { null }, Favourites = null, EmployeeOfTheMonth = null,
                    Players = new List<PlayerOutcome> { new PlayerOutcome { Cards = null } } }
            };
            AssertFields(view, WireCodec.DecodeView(WireCodec.EncodeView(view)), "view");
        }

        [Test]
        public void HighlightPresenceAndNullOrEmptySamplesRoundTripAndHaveDistinctHashes()
        {
            var highlights = new[]
            {
                null,
                new HighlightView { Samples = null },
                new HighlightView { Samples = new List<FootageSighting>() },
                new HighlightView { Samples = new List<FootageSighting> { null } }
            };
            var hashes = new HashSet<string>();
            foreach (HighlightView highlight in highlights)
            {
                var view = new PlayerView { Result = new MatchResultView { Highlight = highlight } };
                AssertFields(view, WireCodec.DecodeView(WireCodec.EncodeView(view)), "view");
                Assert.IsTrue(hashes.Add(WireCodec.HashResult(view.Result)), "Highlight presence and sample-list shape affect the hash.");
            }
        }

        private static Type MessageType(string message)
        {
            switch (message)
            {
                case "view": return typeof(PlayerView);
                case "request": return typeof(ActionRequest);
                case "result": return typeof(ActionResult);
                case "events": return typeof(List<GameEvent>);
                case "lobby": return typeof(LobbyView);
                case "connection": return typeof(ConnectionPayload);
                default: return typeof(WelcomeMessage);
            }
        }

        private static byte[] Encode(string message, object value)
        {
            switch (message)
            {
                case "view": return WireCodec.EncodeView((PlayerView)value);
                case "request": return WireCodec.EncodeRequest((ActionRequest)value);
                case "result": return WireCodec.EncodeResult((ActionResult)value);
                case "events": return WireCodec.EncodeEvents((List<GameEvent>)value);
                case "lobby": return WireCodec.EncodeLobby((LobbyView)value);
                case "connection": return WireCodec.EncodeConnectionPayload((ConnectionPayload)value);
                default: return WireCodec.EncodeWelcome((WelcomeMessage)value);
            }
        }

        private static object Decode(string message, byte[] bytes)
        {
            switch (message)
            {
                case "view": return WireCodec.DecodeView(bytes);
                case "request": return WireCodec.DecodeRequest(bytes);
                case "result": return WireCodec.DecodeResult(bytes);
                case "events": return WireCodec.DecodeEvents(bytes);
                case "lobby": return WireCodec.DecodeLobby(bytes);
                case "connection": return WireCodec.DecodeConnectionPayload(bytes);
                default: return WireCodec.DecodeWelcome(bytes);
            }
        }

        // Reflection is deliberately confined to tests: a new DTO field automatically participates
        // and fails this coverage test until the explicit codec gains its matching field.
        private static object Populate(Type type, ref int serial)
        {
            int value = serial++;
            if (type == typeof(string)) return "Türkçe_İK_şğü_🦆_" + value;
            if (type == typeof(int)) return -1000 - value;
            if (type == typeof(uint)) return uint.MaxValue - (uint)value;
            if (type == typeof(float)) return value + 0.125f;
            if (type == typeof(double)) return value + 0.0625;
            if (type == typeof(bool)) return value % 2 == 0;
            if (type.IsEnum)
            {
                Array values = Enum.GetValues(type);
                return values.GetValue(value % values.Length);
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)Activator.CreateInstance(type);
                for (int i = 0; i < 2; i++) list.Add(Populate(type.GetGenericArguments()[0], ref serial));
                return list;
            }
            if (type.IsArray)
            {
                var array = Array.CreateInstance(type.GetElementType(), 2);
                for (int i = 0; i < 2; i++) array.SetValue(Populate(type.GetElementType(), ref serial), i);
                return array;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var dictionary = (IDictionary)Activator.CreateInstance(type);
                for (int i = 0; i < 2; i++) dictionary.Add(Populate(type.GetGenericArguments()[0], ref serial), Populate(type.GetGenericArguments()[1], ref serial));
                return dictionary;
            }
            object result = Activator.CreateInstance(type);
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                field.SetValue(result, Populate(field.FieldType, ref serial));
            return result;
        }

        private static void AssertFields(object expected, object actual, string path)
        {
            if (expected == null) { Assert.IsNull(actual, path); return; }
            Assert.IsNotNull(actual, path);
            Assert.AreEqual(expected.GetType(), actual.GetType(), path);
            Type type = expected.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string))
            { Assert.AreEqual(expected, actual, path); return; }
            if (expected is IList expectedList)
            {
                var actualList = (IList)actual;
                Assert.AreEqual(expectedList.Count, actualList.Count, path);
                for (int i = 0; i < expectedList.Count; i++) AssertFields(expectedList[i], actualList[i], path + "[" + i + "]");
                return;
            }
            if (expected is IDictionary expectedDictionary)
            {
                var actualDictionary = (IDictionary)actual;
                Assert.AreEqual(expectedDictionary.Count, actualDictionary.Count, path);
                foreach (object key in expectedDictionary.Keys) AssertFields(expectedDictionary[key], actualDictionary[key], path + "[" + key + "]");
                return;
            }
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                AssertFields(field.GetValue(expected), field.GetValue(actual), path + "." + field.Name);
        }

        private static void AssertDetached(object original, object copy, string path)
        {
            if (original == null || original is string || original.GetType().IsValueType) return;
            Assert.AreNotSame(original, copy, path);
            if (original is IList list)
            {
                for (int i = 0; i < list.Count; i++) AssertDetached(list[i], ((IList)copy)[i], path + "[" + i + "]");
                return;
            }
            if (original is IDictionary dictionary)
            {
                foreach (object key in dictionary.Keys) AssertDetached(dictionary[key], ((IDictionary)copy)[key], path + "[" + key + "]");
                return;
            }
            foreach (FieldInfo field in original.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                AssertDetached(field.GetValue(original), field.GetValue(copy), path + "." + field.Name);
        }

        private static object DifferentValue(Type type, object value)
        {
            if (type == typeof(string)) return (string)value + "_changed";
            if (type == typeof(bool)) return !(bool)value;
            if (type == typeof(float)) return (float)value + 1;
            if (type == typeof(double)) return (double)value + 1;
            if (type.IsEnum) return Enum.ToObject(type, (Convert.ToInt32(value) + 1) % Enum.GetValues(type).Length);
            return Convert.ChangeType(Convert.ToInt64(value) + 1, type);
        }

        private static void VisitLeaves(object value, string path, Action<Type, object, Action<object>, string> visit,
            Action<object> assignParent = null)
        {
            if (value is IList list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    int index = i;
                    Type type = list[i].GetType();
                    if (type.IsPrimitive || type.IsEnum || type == typeof(string))
                        visit(type, list[i], changed => list[index] = changed, path + "[" + i + "]");
                    else VisitLeaves(list[i], path + "[" + i + "]", visit, changed => list[index] = changed);
                }
                return;
            }
            foreach (FieldInfo field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Type type = field.FieldType;
                if (type.IsPrimitive || type.IsEnum || type == typeof(string))
                    visit(type, field.GetValue(value), changed =>
                    {
                        field.SetValue(value, changed);
                        assignParent?.Invoke(value);
                    }, path + "." + field.Name);
                // Reflection boxes structs such as Vec3; write the mutated box back through its parents.
                else VisitLeaves(field.GetValue(value), path + "." + field.Name, visit, changed =>
                {
                    field.SetValue(value, changed);
                    assignParent?.Invoke(value);
                });
            }
        }
    }
}
