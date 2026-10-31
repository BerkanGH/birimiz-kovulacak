using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace BirimizKovulacak.Telemetry.Tests
{
    public class SinkTests
    {
        string _dir;

        [SetUp]
        public void Setup() { _dir = Path.Combine(Path.GetTempPath(), "bk_tel_" + Guid.NewGuid().ToString("N")); }

        [TearDown]
        public void Cleanup() { try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (IOException) { } }

        static List<TelemetryEvent> Make(int n, int start = 0)
        {
            var h = new Harness();
            var list = new List<TelemetryEvent>();
            for (int i = 0; i < n; i++)
            {
                h.Service.Begin("phase_exited").Float("duration_seconds", start + i).Enum("completion_reason", "timer").Emit();
            }
            list.AddRange(h.Flushed());
            return list;
        }

        string[] Files() { return Directory.Exists(_dir) ? Directory.GetFiles(_dir, "events_*.jsonl").OrderBy(f => f).ToArray() : new string[0]; }

        [Test]
        public void WritesOneValidJsonObjectPerLine()
        {
            var sink = new LocalJsonlTelemetrySink(_dir, "s-aaaa");
            sink.Write(Make(5));
            sink.Write(Make(3));
            var files = Files();
            Assert.AreEqual(1, files.Length);
            var lines = File.ReadAllLines(files[0]);
            Assert.AreEqual(8, lines.Length);
            foreach (var l in lines) Assert.AreEqual("phase_exited", JsonDocument.Parse(l).RootElement.GetProperty("event").GetString());
            StringAssert.Contains("events_s-aaaa_1.jsonl", files[0]);
        }

        [Test]
        public void RotatesAtSizeLimit_NoFileExceedsIt()
        {
            var sink = new LocalJsonlTelemetrySink(_dir, "s-rot", maxFileBytes: 4096, maxFiles: 100);
            for (int b = 0; b < 10; b++) sink.Write(Make(10, b * 10));
            var files = Files();
            Assert.Greater(files.Length, 3);
            foreach (var f in files) Assert.LessOrEqual(new FileInfo(f).Length, 4096, f);
            int total = files.Sum(f => File.ReadAllLines(f).Length);
            Assert.AreEqual(100, total, "no event lost or duplicated by rotation");
            foreach (var f in files) foreach (var l in File.ReadAllLines(f)) JsonDocument.Parse(l);
        }

        [Test]
        public void KeepsAtMostMaxFiles()
        {
            var sink = new LocalJsonlTelemetrySink(_dir, "s-cap", maxFileBytes: 2048, maxFiles: 5);
            for (int b = 0; b < 40; b++) sink.Write(Make(8, b));
            Assert.LessOrEqual(Files().Length, 5);
            Assert.AreEqual(5, Files().Length);
            Assert.IsTrue(File.Exists(sink.CurrentFile));
        }

        [Test]
        public void RetentionDeletesOldFiles()
        {
            Directory.CreateDirectory(_dir);
            string old = Path.Combine(_dir, "events_s-old_1.jsonl");
            string recent = Path.Combine(_dir, "events_s-new_1.jsonl");
            File.WriteAllText(old, "{}\n"); File.WriteAllText(recent, "{}\n");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-40));
            File.SetLastWriteTimeUtc(recent, DateTime.UtcNow.AddDays(-5));
            File.WriteAllText(Path.Combine(_dir, "notes.txt"), "keep");
            var sink = new LocalJsonlTelemetrySink(_dir, "s-cur", retentionDays: 30);
            sink.Write(Make(1));
            Assert.IsFalse(File.Exists(old));
            Assert.IsTrue(File.Exists(recent));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "notes.txt")), "unrelated files are never touched");
        }

        sealed class TornStream : MemoryStream
        {
            readonly FileStream _real;
            public TornStream(string path) { _real = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read); }
            public override void Write(byte[] buffer, int offset, int count)
            {
                _real.Write(buffer, offset, count * 2 / 5); // part of a batch reaches the disk, ending inside a line
                _real.Flush();
                throw new IOException("device removed");
            }
            protected override void Dispose(bool disposing) { _real.Dispose(); base.Dispose(disposing); }
        }

        [Test]
        public void PartialWrite_AbandonsTornFileAndRecovers()
        {
            bool fail = true;
            var sink = new LocalJsonlTelemetrySink(_dir, "s-torn", openAppend: p => fail ? (Stream)new TornStream(p) : new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.Read));
            Assert.Throws<IOException>(() => sink.Write(Make(6)));
            fail = false;
            sink.Write(Make(4));
            var files = Files();
            Assert.AreEqual(2, files.Length, "the torn file is not appended to again");
            var torn = File.ReadAllText(files[0]);
            Assert.IsFalse(torn.EndsWith("\n"), "the first file ends in a torn line");
            var good = File.ReadAllLines(files[1]);
            Assert.AreEqual(4, good.Length);
            foreach (var l in good) JsonDocument.Parse(l);
            // every complete line of the torn file is still valid JSON
            foreach (var l in torn.Split('\n').Take(torn.Split('\n').Length - 1)) JsonDocument.Parse(l);
        }

        [Test]
        public void ExistingTornFileFromEarlierRun_IsNotAppendedTo()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "events_s-same_1.jsonl"), "{\"schema_version\":1,\"event\":\"phas");
            var sink = new LocalJsonlTelemetrySink(_dir, "s-same");
            sink.Write(Make(2));
            Assert.AreEqual(2, Files().Length);
            Assert.AreEqual("{\"schema_version\":1,\"event\":\"phas", File.ReadAllText(Files()[0]));
            foreach (var l in File.ReadAllLines(Files()[1])) JsonDocument.Parse(l);
        }

        [Test]
        public void IoErrors_AreIsolatedByTheService()
        {
            string blocker = Path.Combine(Path.GetTempPath(), "bk_tel_file_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(blocker, "x");
            try
            {
                var h = new Harness();
                var sink = new LocalJsonlTelemetrySink(Path.Combine(blocker, "sub"), "s-io");
                h.Service.AddSink(sink);
                h.Service.Begin("phase_exited").Float("duration_seconds", 1).Enum("completion_reason", "timer").Emit();
                Assert.DoesNotThrow(() => h.Service.Flush());
                Assert.AreEqual(1, h.Service.GetStats().SinkErrors);
                Assert.AreEqual(1, h.Sink.Count("phase_exited"));
                h.Service.Flush();
                var err = h.Sink.Named("telemetry_sink_error").Single();
                Assert.AreEqual("local_jsonl", err.GetString("sink"));
                Assert.AreEqual("io", err.GetString("category"));
            }
            finally { File.Delete(blocker); }
        }

        [Test]
        public void ServiceWritesThroughSinkEndToEnd()
        {
            var h = new Harness();
            var sink = new LocalJsonlTelemetrySink(_dir, h.Service.Context.SessionId);
            h.Service.AddSink(sink);
            for (int i = 0; i < 10; i++) h.Service.Begin("phase_exited").Float("duration_seconds", i).Enum("completion_reason", "timer").Emit();
            h.Service.Flush();
            var lines = File.ReadAllLines(Files().Single());
            Assert.AreEqual(10, lines.Length);
            Assert.AreEqual(h.Service.Context.SessionId, JsonDocument.Parse(lines[0]).RootElement.GetProperty("session_id").GetString());
        }
    }
}
