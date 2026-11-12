using System;
using System.Collections.Generic;
using System.Text;
using BirimizKovulacak.Core;
using UnityEngine;

namespace BirimizKovulacak.Game
{
    /// <summary>
    /// v5 bot talk, presentation side (docs/V5_BOT_TALK.md). A bot's phone lines and a human's question buttons travel as
    /// small packets (never through the rules engine): bot → host → the listener's client, human → host → the bot (the
    /// host's own bots answer on the host; a -bot process answers in its own process). Meeting chat lines and texts go
    /// through the engine as a token (<see cref="LineBotLines.Token"/>) and are rendered here in the reader's language.
    /// Also keeps the local player's call transcript (the phone shows it), the bots' mumble and talk animation, and the
    /// meeting subtitles' feed.
    /// </summary>
    public static class BotSpeech
    {
        const byte KindLine = 1, KindAsk = 2;

        /// <summary>Client → host (set by NetSession).</summary>
        public static Action<byte[]> Transport;
        /// <summary>A question addressed to this process's automated player (-bot driver).</summary>
        public static event Action<BotAsk> AskReceived;
        /// <summary>A bot line said to me on the phone (UI: typewriter, mumble).</summary>
        public static event Action<Entry> LineHeard;

        // ------------------------------------------------------------------ packets: [kind, week, from, to, ...]

        public static byte[] PackLine(int week, BotLine l)
        {
            var key = Encoding.UTF8.GetBytes(l.Key ?? "");
            var b = new byte[5 + key.Length + 1 + l.Args.Count * 2];
            b[0] = KindLine; b[1] = (byte)week; b[2] = (byte)l.Speaker; b[3] = (byte)Math.Max(0, l.Listener);
            b[4] = (byte)key.Length;
            Array.Copy(key, 0, b, 5, key.Length);
            int i = 5 + key.Length;
            b[i++] = (byte)l.Args.Count;
            foreach (int a in l.Args) { b[i++] = (byte)(a & 0xFF); b[i++] = (byte)((a >> 8) & 0xFF); }
            return b;
        }

        public static byte[] PackAsk(int week, BotAsk a)
        {
            return new[] { KindAsk, (byte)week, (byte)a.From, (byte)a.To, (byte)a.Set, (byte)a.Index };
        }

        /// <summary>Who a packet is from and to (host routing). False for a malformed packet.</summary>
        public static bool Peek(byte[] p, out int week, out int from, out int to, out bool ask)
        {
            week = from = to = -1;
            ask = false;
            if (p == null || p.Length < 6 || (p[0] != KindLine && p[0] != KindAsk)) return false;
            week = p[1]; from = p[2]; to = p[3]; ask = p[0] == KindAsk;
            return from < LineProtocol.MaxPlayers && to < LineProtocol.MaxPlayers;
        }

        public static bool TryAsk(byte[] p, out BotAsk a)
        {
            a = default(BotAsk);
            if (p == null || p.Length < 6 || p[0] != KindAsk) return false;
            a = new BotAsk { From = p[2], To = p[3], Set = (BotQuestionSet)p[4], Index = p[5] };
            return a.Set != BotQuestionSet.None && a.Index < LineBotLines.Questions(a.Set).Count;
        }

        public static bool TryLine(byte[] p, out int from, out int to, out string key, out List<int> args)
        {
            from = to = -1; key = null; args = null;
            if (p == null || p.Length < 6 || p[0] != KindLine) return false;
            int n = p[4];
            if (p.Length < 6 + n) return false;
            from = p[2]; to = p[3];
            key = Encoding.UTF8.GetString(p, 5, n);
            int i = 5 + n, count = p[i++];
            if (p.Length < i + count * 2) return false;
            args = new List<int>(count);
            for (int k = 0; k < count; k++, i += 2) args.Add(p[i] | (p[i + 1] << 8));
            var sig = LineBotLines.Signature(key);
            if (sig == null || sig.Length != count) return false;
            for (int k = 0; k < count; k++) if (sig[k] == 'p' && args[k] >= LineProtocol.MaxPlayers) return false;
            return true;
        }

        // ------------------------------------------------------------------ sending

        public static void SendLine(int week, BotLine l) { Transport?.Invoke(PackLine(week, l)); }
        public static void SendAsk(int week, BotAsk a) { Transport?.Invoke(PackAsk(week, a)); }

        /// <summary>The local player presses question <paramref name="index"/> (1/2/3) on the phone with a bot.</summary>
        public static void Ask(int bot, BotQuestionSet set, int index)
        {
            var lc = LineClient.I;
            if (lc == null || lc.View == null) return;
            var a = new BotAsk { From = lc.MySlot, To = bot, Set = set, Index = index };
            var keys = LineBotLines.Questions(set);
            if (index < 0 || index >= keys.Count) return;
            Add(new Entry { Speaker = lc.MySlot, Key = keys[index], Args = new List<int>(), Mine = true, At = Time.unscaledTime, Other = bot });
            if (MockAnswer != null) MockAnswer(a);
            else SendAsk(lc.View.WeekId, a);
        }

        /// <summary>UI mocks: answers a question locally instead of sending it.</summary>
        public static Action<BotAsk> MockAnswer;

        // ------------------------------------------------------------------ receiving

        /// <summary>A packet from the host for this client.</summary>
        public static void Receive(byte[] p)
        {
            var lc = LineClient.I;
            if (lc == null || p == null || p.Length < 4) return;
            if (p[0] == KindAsk)
            {
                BotAsk a;
                if (TryAsk(p, out a) && a.To == lc.MySlot) AskReceived?.Invoke(a);
                return;
            }
            int from, to;
            string key;
            List<int> args;
            if (!TryLine(p, out from, out to, out key, out args) || to != lc.MySlot) return;
            if (LaunchArgs.Current.Bot) return; // automated players do not read
            Hear(from, key, args);
        }

        /// <summary>A bot line for my phone (network, or a UI mock).</summary>
        public static void Hear(int from, string key, List<int> args)
        {
            var e = new Entry { Speaker = from, Key = key, Args = args ?? new List<int>(), At = Time.unscaledTime, Other = from };
            Add(e);
            Speak(from, Text(key, e.Args), null);
            LineHeard?.Invoke(e);
        }

        // ------------------------------------------------------------------ the local call transcript

        public sealed class Entry
        {
            public int Speaker, Other;
            public string Key;
            public List<int> Args;
            public float At;
            /// <summary>My question (shown on my side).</summary>
            public bool Mine;
        }

        static readonly List<Entry> _transcript = new List<Entry>();
        /// <summary>Lines of my current / last bot call, oldest first (at most 12).</summary>
        public static IReadOnlyList<Entry> Transcript => _transcript;
        /// <summary>Bumped on every change (UI diffing).</summary>
        public static int Version { get; private set; }

        static void Add(Entry e)
        {
            if (_transcript.Count > 0 && _transcript[_transcript.Count - 1].Other != e.Other) _transcript.Clear();
            _transcript.Add(e);
            if (_transcript.Count > 12) _transcript.RemoveAt(0);
            Version++;
        }

        public static void ClearTranscript() { if (_transcript.Count > 0) { _transcript.Clear(); Version++; } }

        /// <summary>Seconds since the last line (a refusal stays on the phone for a moment after the call is gone).</summary>
        public static float LastAge => _transcript.Count == 0 ? float.MaxValue : Time.unscaledTime - _transcript[_transcript.Count - 1].At;

        // ------------------------------------------------------------------ text

        /// <summary>A bot line in the reader's language (names bare: Turkish suffixes never follow a name).</summary>
        public static string Text(string key, List<int> args)
        {
            var sig = LineBotLines.Signature(key) ?? "";
            var a = new object[args != null ? args.Count : 0];
            for (int i = 0; i < a.Length; i++) a[i] = i < sig.Length && sig[i] == 'p' ? (object)PlayText.Name(args[i]) : args[i];
            return Loc.F("v5." + key, a);
        }

        /// <summary>True when a message is a bot line (only bots' tokens are rendered: a person typing one gets it verbatim).</summary>
        public static bool IsBotLine(LineMessage m, out string key, out List<int> args)
        {
            key = null; args = null;
            if (m == null || string.IsNullOrEmpty(m.Text) || !m.Text.StartsWith(LineBotLines.TokenPrefix, StringComparison.Ordinal)) return false;
            var p = LineClient.I != null ? LineClient.I.Player(m.From) : null;
            if (p == null || !p.IsBot) return false;
            return LineBotLines.TryParse(m.Text, out key, out args);
        }

        /// <summary>The text to show for a message: a bot line in my language, else what the sender typed.</summary>
        public static string MessageText(LineMessage m)
        {
            string key;
            List<int> args;
            return IsBotLine(m, out key, out args) ? Text(key, args) : (m != null ? m.Text ?? "" : "");
        }

        // ------------------------------------------------------------------ voice: mumble and the talk animation

        static readonly float[] _talkFrom = new float[8], _talkUntil = new float[8];

        /// <summary>Mumble (a bean-like babble pitched per colleague) and the talk animation for the length of the line.</summary>
        public static void Speak(int slot, string text, Vector3? at)
        {
            if (slot < 0 || slot >= _talkUntil.Length) return;
            float seconds = Mathf.Clamp((text != null ? text.Length : 20) * 0.045f, 0.8f, 3.2f);
            _talkFrom[slot] = Time.unscaledTime;
            _talkUntil[slot] = Time.unscaledTime + seconds;
            var lc = LineClient.I;
            int colour = lc != null ? lc.ColorOf(slot) : slot;
            float pitch = 0.82f + 0.09f * (colour % 6);
            Sfx.Mumble(seconds, pitch, slot, at);
        }

        /// <summary>0..1 "voice level" of a bot saying a line (drives the beans' talk animation and the shared gaze).</summary>
        public static float Level(int slot)
        {
            if (slot < 0 || slot >= _talkUntil.Length) return 0f;
            float now = Time.unscaledTime;
            if (now >= _talkUntil[slot]) return 0f;
            float t = now - _talkFrom[slot];
            return 0.35f + 0.3f * Mathf.Abs(Mathf.Sin(t * 13f)) + 0.15f * Mathf.Abs(Mathf.Sin(t * 5.3f + slot));
        }

        public static void ResetVoices() { for (int i = 0; i < _talkUntil.Length; i++) _talkUntil[i] = 0f; }
    }
}
