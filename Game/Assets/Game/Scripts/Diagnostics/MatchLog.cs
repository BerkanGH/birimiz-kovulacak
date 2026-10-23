using System;
using System.IO;
using System.Text;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// Plain-text session log used by the automated multi-process verification (Tools/scripts/verify_logs.py)
    /// and for bug reports. One file per process: &lt;logdir&gt;/&lt;name&gt;_&lt;pid&gt;.log. Never contains another
    /// player's private data except on the host when -debugtruth is passed (host truth dump at results).
    /// </summary>
    public static class MatchLog
    {
        static StreamWriter _writer;
        static string _dir;
        public static string Directory => _dir;

        public static void Initialize(string playerName)
        {
            try
            {
                _dir = LaunchArgs.Current.LogDir;
                if (string.IsNullOrEmpty(_dir)) _dir = Path.Combine(Application.persistentDataPath, "logs");
                System.IO.Directory.CreateDirectory(_dir);
                string safe = string.IsNullOrEmpty(playerName) ? "player" : MakeSafe(playerName);
                string path = Path.Combine(_dir, safe + "_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".log");
                _writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
                Write("start build=" + GameConfig.BuildId + " unity=" + Application.unityVersion + " args=" + string.Join(" ", Environment.GetCommandLineArgs()));
                Application.logMessageReceived += OnUnityLog;
            }
            catch (Exception e) { Debug.LogWarning("[MatchLog] " + e.Message); }
        }

        static string MakeSafe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }

        static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
                Write("unity-" + type + " " + condition.Replace('\n', ' ') + " | " + (stackTrace ?? "").Split('\n')[0]);
        }

        public static void Write(string line)
        {
            if (_writer == null) return;
            try { _writer.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + line); }
            catch (Exception) { }
        }

        public static void Event(GameEvent e) { Write("event " + e); }

        public static void WriteFile(string name, string content)
        {
            if (string.IsNullOrEmpty(_dir)) return;
            try { File.WriteAllText(Path.Combine(_dir, name), content, new UTF8Encoding(false)); }
            catch (Exception ex) { Debug.LogWarning("[MatchLog] " + ex.Message); }
        }

        public static void Close()
        {
            if (_writer == null) return;
            Write("end");
            _writer.Dispose();
            _writer = null;
        }
    }
}
