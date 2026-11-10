// Compile-only stand-in for Game/Assets/Game/Core/Line/Engine/** (python -I Tools/scripts/compile_check.py --engine-stubs).
// It lists the public engine API that Unity scripts may use; the real engine must provide exactly these members.
// Every method throws: never ship or run this file.
using System;
using System.Collections.Generic;

namespace BirimizKovulacak.Core
{
    public static class LineEngineFactory
    {
        public static ILineEngine Create(LineSetup setup, LineConfig config = null) { throw new NotImplementedException(); }
    }

    public static class LineCodec
    {
        public static byte[] EncodeView(LineView v) { throw new NotImplementedException(); }
        public static LineView DecodeView(byte[] b) { throw new NotImplementedException(); }
        public static byte[] EncodeAction(LineAction a) { throw new NotImplementedException(); }
        public static LineAction DecodeAction(byte[] b) { throw new NotImplementedException(); }
        public static byte[] EncodeEvents(List<LineEvent> e) { throw new NotImplementedException(); }
        public static List<LineEvent> DecodeEvents(byte[] b) { throw new NotImplementedException(); }
        public static byte[] EncodeResult(LineActionResult r) { throw new NotImplementedException(); }
        public static LineActionResult DecodeResult(byte[] b) { throw new NotImplementedException(); }
        public static byte[] EncodeLobby(LineLobbyView l) { throw new NotImplementedException(); }
        public static LineLobbyView DecodeLobby(byte[] b) { throw new NotImplementedException(); }
        public static byte[] EncodeWelcome(LineWelcome w) { throw new NotImplementedException(); }
        public static LineWelcome DecodeWelcome(byte[] b) { throw new NotImplementedException(); }
        public static byte[] EncodeConnection(LineConnectionPayload p) { throw new NotImplementedException(); }
        public static LineConnectionPayload DecodeConnection(byte[] b) { throw new NotImplementedException(); }
        public static byte[] EncodeMessages(List<LineMessage> m) { throw new NotImplementedException(); }
        public static List<LineMessage> DecodeMessages(byte[] b) { throw new NotImplementedException(); }
    }

    public enum LineBotPolicy : byte { Honest = 0, Greedy = 1, Mixed = 2 }

    public sealed class LineBot
    {
        public LineBot(int seat, int seed, LineBotPolicy policy = LineBotPolicy.Honest) { }
        public double ThinkSeconds;
        public double WakeAt { get { throw new NotImplementedException(); } }
        public LineBotPolicy Policy { get { throw new NotImplementedException(); } }
        public LineAction Next(LineView v, LineConfig config, double now) { throw new NotImplementedException(); }
    }

    /// <summary>Player-authored text rules (v4 behaviour kept in v5), shared by the engine and the host's name sanitiser.</summary>
    public static class LineText
    {
        public static string Sanitize(string text, int maxChars, bool allowNewlines, out LineFailure failure) { throw new NotImplementedException(); }
    }
}
