using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BirimizKovulacak.Telemetry
{
    /// <summary>
    /// One JSON object per line into <c>events_&lt;session&gt;_&lt;n&gt;.jsonl</c>. Bounded: files rotate at
    /// <c>maxFileBytes</c>, at most <c>maxFiles</c> files and <c>retentionDays</c> days are kept per directory.
    /// A failed or partial write abandons the file (the next batch starts a new one) so a torn line can only be the
    /// last line of an old file, which the analyzer tolerates. The directory is injected (no UnityEngine).
    /// </summary>
    public sealed class LocalJsonlTelemetrySink : ITelemetrySink
    {
        readonly string _dir;
        readonly string _session;
        readonly int _maxBytes, _maxFiles, _retentionDays;
        readonly Func<DateTime> _utcNow;
        readonly Func<string, Stream> _open;
        readonly object _lock = new object();
        readonly StringBuilder _sb = new StringBuilder(2048);
        int _index;
        string _current;
        long _currentSize;
        bool _pruned;

        public LocalJsonlTelemetrySink(string directory, string sessionId, int maxFileBytes = 1024 * 1024, int maxFiles = 20,
            int retentionDays = 30, Func<DateTime> utcNow = null, Func<string, Stream> openAppend = null)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("directory");
            _dir = directory;
            _session = string.IsNullOrEmpty(sessionId) ? "s-unknown" : sessionId;
            _maxBytes = Math.Max(1024, maxFileBytes);
            _maxFiles = Math.Max(1, maxFiles);
            _retentionDays = Math.Max(1, retentionDays);
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _open = openAppend ?? (p => new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.Read));
        }

        public string Name { get { return "local_jsonl"; } }
        public TelemetrySinkKind Kind { get { return TelemetrySinkKind.Local; } }
        public string Directory { get { return _dir; } }
        public string CurrentFile { get { return _current; } }

        public const string Pattern = "events_*.jsonl";

        public void Write(IReadOnlyList<TelemetryEvent> batch)
        {
            if (batch == null || batch.Count == 0) return;
            lock (_lock)
            {
                System.IO.Directory.CreateDirectory(_dir);
                if (!_pruned) { _pruned = true; Prune(null); }
                var chunk = new MemoryStream(4096);
                for (int i = 0; i < batch.Count; i++)
                {
                    _sb.Clear();
                    TelemetryJson.Write(_sb, batch[i]);
                    _sb.Append('\n');
                    byte[] line = Encoding.UTF8.GetBytes(_sb.ToString());
                    long size = _current == null ? 0 : _currentSize;
                    if (size + chunk.Length + line.Length > _maxBytes && size + chunk.Length > 0)
                    {
                        AppendChunk(chunk);
                        chunk.SetLength(0);
                        _current = null; // rotate
                    }
                    chunk.Write(line, 0, line.Length);
                }
                if (chunk.Length > 0) AppendChunk(chunk);
            }
        }

        void AppendChunk(MemoryStream chunk)
        {
            if (chunk.Length == 0) return;
            if (_current == null) StartNewFile();
            try
            {
                using (var fs = _open(_current))
                {
                    var buf = chunk.GetBuffer();
                    fs.Write(buf, 0, (int)chunk.Length);
                    fs.Flush();
                }
                _currentSize += chunk.Length;
            }
            catch
            {
                _current = null; // abandon a possibly torn file; the next batch opens a fresh one
                throw;
            }
        }

        void StartNewFile()
        {
            string path;
            do { _index++; path = Path.Combine(_dir, "events_" + _session + "_" + _index + ".jsonl"); }
            while (File.Exists(path));
            _current = path;
            _currentSize = 0;
            Prune(path);
        }

        /// <summary>Deletes files older than the retention window, then the oldest files beyond the file cap (never the current one).</summary>
        public void Prune(string keep)
        {
            try
            {
                var files = new List<FileInfo>(new DirectoryInfo(_dir).GetFiles(Pattern));
                DateTime now = _utcNow();
                for (int i = files.Count - 1; i >= 0; i--)
                {
                    if (keep != null && string.Equals(files[i].FullName, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    if ((now - files[i].LastWriteTimeUtc).TotalDays > _retentionDays) { TryDelete(files[i]); files.RemoveAt(i); }
                }
                files.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                int excess = files.Count + (keep != null && !files.Exists(f => string.Equals(f.FullName, keep, StringComparison.OrdinalIgnoreCase)) ? 1 : 0) - _maxFiles;
                for (int i = 0; i < files.Count && excess > 0; i++)
                {
                    if (keep != null && string.Equals(files[i].FullName, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    if (TryDelete(files[i])) excess--;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static bool TryDelete(FileInfo f)
        {
            try { f.Delete(); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public void Flush() { }
        public void Dispose() { }
    }
}
